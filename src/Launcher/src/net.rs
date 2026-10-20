//! Friends over the network: this PC's addresses, the Windows firewall, the router port (UPnP), friends hosting
//! on the same network (UDP beacon), Steam's server query, and invite codes.

use crate::server::{self, ServerSettings, PORT};
use crate::settings::JoinTarget;
use base64::Engine;
use regex::Regex;
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};
use std::net::{IpAddr, Ipv4Addr, SocketAddr, ToSocketAddrs, UdpSocket};
use std::sync::{LazyLock, Mutex, OnceLock};
use std::time::{Duration, Instant};

// -------------------------------------------------------------------------------------------------
// Addresses
// -------------------------------------------------------------------------------------------------

/// An IPv4 address of this PC that a friend could connect to.
#[derive(Clone, Debug)]
pub struct LocalAddress {
    pub address: Ipv4Addr,
    pub kind: &'static str,
    pub adapter: String,
}

impl LocalAddress {
    pub fn is_vpn(&self) -> bool {
        self.kind != "Wi-Fi / LAN"
    }
}

struct Nic {
    name: String,
    text: String,
    up: bool,
    ips: Vec<(Ipv4Addr, Ipv4Addr)>,
    gateways: Vec<Ipv4Addr>,
}

fn nics() -> Vec<Nic> {
    netdev::get_interfaces()
        .into_iter()
        .map(|i| {
            let name = i.friendly_name.clone().unwrap_or_else(|| i.name.clone());
            Nic {
                text: format!("{} {name}", i.description.clone().unwrap_or_default()),
                up: i.is_up() && !i.is_loopback(),
                ips: i.ipv4.iter().map(|n| (n.addr(), n.netmask())).collect(),
                gateways: i.gateway.as_ref().map(|g| g.ipv4.clone()).unwrap_or_default(),
                name,
            }
        })
        .collect()
}

/// What kind of network an address is on, or None for addresses friends can't use.
pub fn classify(address: Ipv4Addr, adapter: &str) -> Option<&'static str> {
    let b = address.octets();
    let named = |n: &str| adapter.to_lowercase().contains(&n.to_lowercase());
    if b[0] == 127 || (b[0] == 169 && b[1] == 254) {
        return None;
    }
    if b[0] == 26 || named("Radmin") {
        return Some("Radmin VPN");
    }
    if b[0] == 25 || named("Hamachi") {
        return Some("Hamachi");
    }
    if named("ZeroTier") {
        return Some("ZeroTier");
    }
    if b[0] == 100 && (64..128).contains(&b[1]) {
        return Some("Tailscale / VPN");
    }
    if b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && (16..32).contains(&b[1])) {
        return Some("Wi-Fi / LAN");
    }
    None // a public address: don't suggest it (that's port forwarding, not this)
}

/// Every address friends could use, VPN first (that's what friends over the internet need), then LAN.
pub fn addresses() -> Vec<LocalAddress> {
    let mut list: Vec<LocalAddress> = nics()
        .into_iter()
        .filter(|n| n.up)
        .flat_map(|n| n.ips.iter().filter_map(|(ip, _)| Some(LocalAddress { address: *ip, kind: classify(*ip, &n.text)?, adapter: n.name.clone() })).collect::<Vec<_>>())
        .collect();
    list.sort_by_key(|a| !a.is_vpn());
    list
}

/// Every address of this PC, on any adapter (to ignore our own broadcasts).
fn own_addresses() -> HashSet<Ipv4Addr> {
    nics().into_iter().flat_map(|n| n.ips.into_iter().map(|i| i.0)).collect()
}

/// The broadcast address of every IPv4 network this PC is on (LAN, Wi-Fi, Radmin...).
fn broadcast_addresses() -> Vec<Ipv4Addr> {
    let mut list = vec![Ipv4Addr::BROADCAST];
    for n in nics().into_iter().filter(|n| n.up) {
        for (ip, mask) in n.ips {
            let b = Ipv4Addr::from(u32::from(ip) | !u32::from(mask));
            if !list.contains(&b) {
                list.push(b);
            }
        }
    }
    list
}

/// Our LAN addresses (not VPNs) with their adapter's default gateways.
fn lan_addresses() -> Vec<(Ipv4Addr, Vec<Ipv4Addr>)> {
    nics()
        .into_iter()
        .filter(|n| n.up)
        .flat_map(|n| n.ips.iter().filter(|(ip, _)| classify(*ip, &n.text) == Some("Wi-Fi / LAN")).map(|(ip, _)| (*ip, n.gateways.clone())).collect::<Vec<_>>())
        .collect()
}

// -------------------------------------------------------------------------------------------------
// Windows firewall
// -------------------------------------------------------------------------------------------------

pub const FIREWALL_RULE: &str = "LAN Skin Tester (CS2)";

#[cfg(windows)]
fn hidden(cmd: &mut std::process::Command) -> &mut std::process::Command {
    std::os::windows::process::CommandExt::creation_flags(cmd, 0x0800_0000) // CREATE_NO_WINDOW
}

/// True if our firewall rule is there (reading rules needs no admin).
pub fn firewall_rule_exists() -> bool {
    #[cfg(windows)]
    {
        let out = hidden(std::process::Command::new("netsh").args(["advfirewall", "firewall", "show", "rule", &format!("name={FIREWALL_RULE}")])).output();
        out.is_ok_and(|o| o.status.success() && String::from_utf8_lossy(&o.stdout).contains(FIREWALL_RULE))
    }
    #[cfg(not(windows))]
    false
}

/// Runs a PowerShell script hidden; given as UTF-16 base64 so quotes and non-ASCII folders survive.
pub fn powershell(script: &str, wait: bool) -> std::io::Result<Option<i32>> {
    let utf16: Vec<u8> = script.encode_utf16().flat_map(u16::to_le_bytes).collect();
    let encoded = base64::engine::general_purpose::STANDARD.encode(utf16);
    let mut cmd = std::process::Command::new("powershell.exe");
    cmd.args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-EncodedCommand", &encoded]);
    #[cfg(windows)]
    hidden(&mut cmd);
    let mut child = cmd.spawn()?;
    Ok(if wait { child.wait()?.code() } else { None })
}

/// PowerShell's '…' literal.
pub fn ps_quote(text: &str) -> String {
    format!("'{}'", text.replace('\'', "''"))
}

/// Adds an inbound firewall rule for cs2.exe (asks for admin). False if the user said no.
pub fn allow_through_firewall(exe: &std::path::Path) -> bool {
    let args = format!(
        "/c netsh advfirewall firewall delete rule name=\"{FIREWALL_RULE}\" >nul & netsh advfirewall firewall add rule name=\"{FIREWALL_RULE}\" dir=in action=allow program=\"{}\" enable=yes profile=any",
        exe.display()
    );
    let script = format!("$p = Start-Process -FilePath cmd.exe -ArgumentList {} -Verb RunAs -WindowStyle Hidden -Wait -PassThru; exit $p.ExitCode", ps_quote(&args));
    matches!(powershell(&script, true), Ok(Some(0)))
}

// -------------------------------------------------------------------------------------------------
// UPnP: opens the server port on the home router (plain SSDP + SOAP)
// -------------------------------------------------------------------------------------------------

/// A router found with UPnP: where to send commands and which of our addresses it sees.
#[derive(Clone, Debug)]
pub struct Router {
    control_url: String,
    service_type: String,
    local: Ipv4Addr,
    pub name: String,
}

const LEASE_SECONDS: u64 = 7200;
const SERVICE_TYPES: [&str; 3] = [
    "urn:schemas-upnp-org:service:WANIPConnection:2",
    "urn:schemas-upnp-org:service:WANIPConnection:1",
    "urn:schemas-upnp-org:service:WANPPPConnection:1",
];

fn upnp_http() -> &'static ureq::Agent {
    static HTTP: OnceLock<ureq::Agent> = OnceLock::new();
    HTTP.get_or_init(|| ureq::Agent::config_builder().timeout_global(Some(Duration::from_secs(6))).proxy(None).http_status_as_error(false).build().into())
}

/// The router's state, shared by every way of starting the server.
#[derive(Default)]
pub struct RouterState {
    pub router: Option<Router>,
    pub external: Option<Ipv4Addr>,
    /// A search has finished (whether or not a router answered).
    pub checked: bool,
    pub searching: bool,
    /// The mapping this launcher made (removed again on Stop / close).
    active: Option<(Router, Instant)>,
}

impl RouterState {
    pub fn port_open(&self) -> bool {
        self.active.is_some()
    }
}

pub static ROUTER: LazyLock<Mutex<RouterState>> = LazyLock::new(Default::default);
/// Search started or finished, or the port was opened / closed: pages refresh.
pub static ROUTER_CHANGED: OnceLock<Box<dyn Fn() + Send + Sync>> = OnceLock::new();

fn router_changed() {
    if let Some(f) = ROUTER_CHANGED.get() {
        f();
    }
}

/// The router's "internet" address is itself private (CGNAT or a second router): friends can't reach it.
pub fn behind_another_nat(ip: Ipv4Addr) -> bool {
    let b = ip.octets();
    b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && (16..32).contains(&b[1]))
        || (b[0] == 100 && (64..128).contains(&b[1])) || (b[0] == 169 && b[1] == 254)
}

fn header(response: &str, name: &str) -> Option<String> {
    response.split("\r\n").find_map(|l| {
        let (k, v) = l.split_once(':')?;
        k.trim().eq_ignore_ascii_case(name).then(|| v.trim().to_string())
    })
}

fn ssdp_search(wait: Duration) -> Vec<(String, Ipv4Addr)> {
    let mut sockets = vec![];
    for (local, gateways) in lan_addresses() {
        let Ok(udp) = UdpSocket::bind((local, 0)) else { continue };
        for st in ["urn:schemas-upnp-org:device:InternetGatewayDevice:1", "upnp:rootdevice"] {
            let request = format!("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n");
            // Multicast, plus straight to the gateway (some routers only answer that).
            let _ = udp.send_to(request.as_bytes(), ("239.255.255.250", 1900));
            for g in &gateways {
                let _ = udp.send_to(request.as_bytes(), (*g, 1900));
            }
        }
        let _ = udp.set_read_timeout(Some(Duration::from_millis(50)));
        sockets.push((udp, local));
    }
    let mut found: Vec<(String, Ipv4Addr)> = vec![];
    let deadline = Instant::now() + wait;
    let mut buf = [0u8; 4096];
    while !sockets.is_empty() && Instant::now() < deadline {
        for (udp, local) in &sockets {
            if let Ok((n, _)) = udp.recv_from(&mut buf) {
                if let Some(location) = header(&String::from_utf8_lossy(&buf[..n]), "LOCATION") {
                    if !found.iter().any(|f| f.0 == location) {
                        found.push((location, *local));
                    }
                }
            }
        }
    }
    found
}

fn xml_value(xml: &str, name: &str) -> Option<String> {
    let doc = roxmltree::Document::parse(xml).ok()?;
    let v = doc.descendants().find(|n| n.tag_name().name() == name)?.text()?.trim().to_string();
    Some(v)
}

fn read_description(location: &str, local: Ipv4Addr) -> Option<Router> {
    let xml = crate::util::get_text(upnp_http(), location).ok()?;
    let doc = roxmltree::Document::parse(&xml).ok()?;
    let text = |n: roxmltree::Node, name: &str| n.children().find(|c| c.tag_name().name() == name).and_then(|c| c.text()).map(|t| t.trim().to_string());
    let base = doc.descendants().find(|n| n.tag_name().name() == "URLBase").and_then(|n| n.text()).map(str::trim).filter(|b| b.starts_with("http"))
        .unwrap_or(location).to_string();
    let host = base.splitn(4, '/').take(3).collect::<Vec<_>>().join("/");
    let name = doc.descendants().find(|n| n.tag_name().name() == "friendlyName").and_then(|n| n.text()).map(|t| t.trim().to_string())
        .unwrap_or_else(|| host.clone());
    let services: Vec<_> = doc.descendants().filter(|n| n.tag_name().name() == "service").collect();
    for ty in SERVICE_TYPES {
        let Some(service) = services.iter().find(|s| text(**s, "serviceType").as_deref() == Some(ty)) else { continue };
        let Some(control) = text(*service, "controlURL") else { continue };
        let url = if control.starts_with("http") {
            control
        } else if control.starts_with('/') {
            format!("{host}{control}")
        } else {
            format!("{}/{control}", base.trim_end_matches('/'))
        };
        return Some(Router { control_url: url, service_type: ty.into(), local, name });
    }
    None
}

/// A SOAP call; the error is (UPnP error code, message).
fn soap(router: &Router, action: &str, args: &[(&str, String)]) -> Result<String, (i32, String)> {
    let escape = |s: &str| s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;").replace('\'', "&apos;");
    let body = format!(
        "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:{action} xmlns:u=\"{}\">{}</u:{action}></s:Body></s:Envelope>",
        router.service_type,
        args.iter().map(|(n, v)| format!("<{n}>{}</{n}>", escape(v))).collect::<String>()
    );
    let mut r = upnp_http()
        .post(&router.control_url)
        .header("Content-Type", "text/xml; charset=utf-8")
        .header("SOAPAction", &format!("\"{}#{action}\"", router.service_type))
        .send(&body)
        .map_err(|e| (0, e.to_string()))?;
    let status = r.status();
    let text = r.body_mut().read_to_string().unwrap_or_default();
    if roxmltree::Document::parse(&text).is_err() {
        return Err((0, format!("{action}: HTTP {}", status.as_u16())));
    }
    if !status.is_success() {
        let code = xml_value(&text, "errorCode").and_then(|c| c.parse().ok()).unwrap_or(0);
        let why = xml_value(&text, "errorDescription").unwrap_or_else(|| format!("HTTP {}", status.as_u16()));
        return Err((code, format!("{action}: {why} ({code})")));
    }
    Ok(text)
}

/// Plain-language explanation for the common router errors.
fn friendly((code, message): (i32, String)) -> String {
    match code {
        718 => format!("Port {PORT} is already forwarded to another PC on your network."),
        606 | 401 => "The router refused (UPnP may be read-only in its settings).".into(),
        _ => message,
    }
}

fn add_mapping(router: &Router) -> Result<(), (i32, String)> {
    let args = |lease: u64| {
        vec![
            ("NewRemoteHost", String::new()), ("NewExternalPort", PORT.to_string()), ("NewProtocol", "UDP".into()),
            ("NewInternalPort", PORT.to_string()), ("NewInternalClient", router.local.to_string()), ("NewEnabled", "1".into()),
            ("NewPortMappingDescription", "LAN Skin Tester (CS2)".into()), ("NewLeaseDuration", lease.to_string()),
        ]
    };
    match soap(router, "AddPortMapping", &args(LEASE_SECONDS)) {
        // Some routers only take permanent mappings (we remove it on Stop).
        Err((725 | 402 | 501, _)) => soap(router, "AddPortMapping", &args(0)).map(drop),
        r => r.map(drop),
    }?;
    ROUTER.lock().unwrap().active = Some((router.clone(), Instant::now()));
    Ok(())
}

/// Re-adds the mapping before its lease runs out. Called now and then while hosting.
pub fn renew_if_needed() {
    let due = ROUTER.lock().unwrap().active.as_ref().filter(|a| a.1.elapsed() > Duration::from_secs(LEASE_SECONDS / 2)).map(|a| a.0.clone());
    if let Some(router) = due {
        let _ = add_mapping(&router);
    }
}

/// Best effort, for Stop and closing the launcher (the lease runs out by itself otherwise).
pub fn remove_mapping() {
    let Some((router, _)) = ROUTER.lock().unwrap().active.take() else { return };
    let _ = soap(&router, "DeletePortMapping", &[("NewRemoteHost", String::new()), ("NewExternalPort", PORT.to_string()), ("NewProtocol", "UDP".into())]);
}

/// Look for the router again next time (e.g. the user switched to Internet mode).
pub fn forget_search() {
    ROUTER.lock().unwrap().checked = false;
}

/// Looks for the router; while a search runs, callers wait for that one instead of seeing no router yet.
pub fn find_router(log: &dyn Fn(String)) {
    {
        let mut r = ROUTER.lock().unwrap();
        if r.searching {
            drop(r);
            while ROUTER.lock().unwrap().searching {
                std::thread::sleep(Duration::from_millis(100));
            }
            return;
        }
        r.searching = true;
    }
    router_changed();
    let router = ssdp_search(Duration::from_secs(3)).into_iter().find_map(|(location, local)| read_description(&location, local));
    let external = router.as_ref().and_then(|r| match soap(r, "GetExternalIPAddress", &[]) {
        Ok(xml) => xml_value(&xml, "NewExternalIPAddress").and_then(|ip| ip.parse().ok()),
        Err(e) => {
            log(format!("UPnP: {}", friendly(e)));
            None
        }
    });
    if let Some(r) = &router {
        log(format!("UPnP router: {}, internet address {}.", r.name, external.map_or("unknown".into(), |e: Ipv4Addr| e.to_string())));
    }
    {
        let mut r = ROUTER.lock().unwrap();
        (r.router, r.external, r.searching, r.checked) = (router, external, false, true);
    }
    router_changed();
}

/// Opens the server port on the router. Returns an error message, or None when it's open.
pub fn open_port(log: &dyn Fn(String)) -> Option<String> {
    if ROUTER.lock().unwrap().active.is_some() {
        return None;
    }
    let missing = { let r = ROUTER.lock().unwrap(); r.router.is_none() || r.external.is_none() };
    if missing {
        find_router(log);
    }
    let (router, external) = { let r = ROUTER.lock().unwrap(); (r.router.clone(), r.external) };
    let Some(router) = router else { return Some("No UPnP router answered. Turn on UPnP in your router's settings, or use Radmin.".into()) };
    if let Some(ip) = external.filter(|ip| behind_another_nat(*ip)) {
        return Some(format!("Your router's internet address ({ip}) is a shared/private one (CGNAT or a second router), so friends can't reach it. Use Radmin instead."));
    }
    let result = match add_mapping(&router) {
        Ok(()) => {
            log(format!("UPnP: opened UDP port {PORT} on {}. It closes again when you press Stop.", router.name));
            None
        }
        Err((0, e)) => Some(format!("The router didn't answer: {e}")),
        Err(e) => Some(friendly(e)),
    };
    router_changed();
    result
}

pub fn close_port(log: &dyn Fn(String)) {
    remove_mapping();
    log("UPnP: closed the router port.".into());
    router_changed();
}

// -------------------------------------------------------------------------------------------------
// Friends hosting on this network (UDP beacon) and Steam's server query
// -------------------------------------------------------------------------------------------------

pub const BEACON_PORT: u16 = 27098;
const MAGIC: &str = "LSTHOST1";

/// A friend hosting on the same network (LAN or Radmin), heard from their launcher.
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LanHost {
    pub name: String,
    pub address: String,
    pub map: String,
    pub players: i32,
    pub max_players: i32,
    pub password: bool,
    #[serde(skip)]
    seen: Instant,
}

/// The beacon's wire format (older launchers send and expect these names).
#[derive(Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Beacon {
    name: String,
    port: u16,
    map: String,
    players: i32,
    max: i32,
    password: bool,
}

static HOSTS: LazyLock<Mutex<HashMap<String, LanHost>>> = LazyLock::new(Default::default);

fn clean(text: &str) -> String {
    text.chars().filter(|c| !c.is_control()).take(40).collect()
}

/// Friends hosting on this PC's networks, heard in the last 10 seconds.
pub fn lan_hosts() -> Vec<LanHost> {
    let mut list: Vec<LanHost> = HOSTS.lock().unwrap().values().filter(|h| h.seen.elapsed() < Duration::from_secs(10)).cloned().collect();
    list.sort_by(|a, b| a.name.cmp(&b.name));
    list
}

pub fn start_listening(log: impl Fn(String) + Send + 'static) {
    std::thread::spawn(move || {
        let udp = match UdpSocket::bind(("0.0.0.0", BEACON_PORT)) {
            Ok(u) => u,
            Err(e) => return log(format!("Friends list: can't listen for friends hosting on your network ({e}).")),
        };
        let mut buf = [0u8; 2048];
        loop {
            let Ok((n, SocketAddr::V4(from))) = udp.recv_from(&mut buf) else {
                std::thread::sleep(Duration::from_millis(200));
                continue;
            };
            let text = String::from_utf8_lossy(&buf[..n]);
            // Noise, or our own announcement (addresses looked up each time: a VPN may connect later).
            let Some(json) = text.strip_prefix(MAGIC) else { continue };
            if own_addresses().contains(from.ip()) {
                continue;
            }
            let Ok(b) = serde_json::from_str::<Beacon>(json) else { continue };
            let address = format!("{}:{}", from.ip(), b.port);
            let mut hosts = HOSTS.lock().unwrap();
            hosts.insert(address.clone(), LanHost { name: clean(&b.name), address, map: clean(&b.map), players: b.players, max_players: b.max, password: b.password, seen: Instant::now() });
            // Servers that stopped announcing a minute ago are gone for good.
            hosts.retain(|_, h| h.seen.elapsed() < Duration::from_secs(60));
        }
    });
}

/// What this PC announces while it hosts: name, map, players, max players, password.
pub type BeaconInfo = (String, String, i32, i32, bool);

/// Announces this server every 3 seconds while `current` returns one.
pub fn start_beacon(current: impl Fn() -> Option<BeaconInfo> + Send + 'static) {
    std::thread::spawn(move || {
        let Ok(udp) = UdpSocket::bind(("0.0.0.0", 0)) else { return };
        let _ = udp.set_broadcast(true);
        std::thread::sleep(Duration::from_secs(2));
        loop {
            if let Some((name, map, players, max, password)) = current() {
                let bytes = format!("{MAGIC}{}", serde_json::to_string(&Beacon { name, port: PORT, map, players, max, password }).unwrap());
                for target in broadcast_addresses() {
                    let _ = udp.send_to(bytes.as_bytes(), (target, BEACON_PORT));
                }
            }
            std::thread::sleep(Duration::from_secs(3));
        }
    });
}

/// What a server says about itself (Steam's A2S_INFO query).
#[derive(Clone, Debug)]
pub struct ServerInfo {
    pub map: String,
    pub players: i32,
    pub max_players: i32,
    pub bots: i32,
    pub password: bool,
    pub ping: u128,
}

/// Asks a server for its map and players (A2S_INFO). None when it doesn't answer.
pub fn query(address: &str) -> Option<ServerInfo> {
    let (host, port) = address.split_once(':').unwrap_or((address, ""));
    let port: u16 = if port.is_empty() { PORT } else { port.parse().ok()? };
    let ip = (host, port).to_socket_addrs().ok()?.find(|a| matches!(a.ip(), IpAddr::V4(_)))?;
    let udp = UdpSocket::bind(("0.0.0.0", 0)).ok()?;
    udp.connect(ip).ok()?;
    udp.set_read_timeout(Some(Duration::from_millis(1500))).ok()?;
    let mut request = vec![0xFF, 0xFF, 0xFF, 0xFF, 0x54];
    request.extend_from_slice(b"Source Engine Query\0");
    let mut buf = [0u8; 1400];
    for _ in 0..2 {
        let sent = Instant::now();
        udp.send(&request).ok()?;
        let n = udp.recv(&mut buf).ok()?;
        let reply = &buf[..n];
        if reply.len() < 5 {
            return None;
        }
        if reply[4] == 0x41 && reply.len() >= 9 {
            request.extend_from_slice(&reply[5..9]); // challenge: send again with it
            continue;
        }
        return if reply[4] == 0x49 { parse_info(reply).map(|i| ServerInfo { ping: sent.elapsed().as_millis(), ..i }) } else { None };
    }
    None
}

/// The A2S_INFO reply: header, protocol, then name, map, folder and game strings, app id and counts.
fn parse_info(reply: &[u8]) -> Option<ServerInfo> {
    let mut i = 6;
    let mut string = || {
        let start = i;
        while i < reply.len() && reply[i] != 0 {
            i += 1;
        }
        i += 1;
        String::from_utf8_lossy(&reply[start..(i - 1).min(reply.len())]).into_owned()
    };
    let _name = string();
    let map = string();
    string(); // folder
    string(); // game
    i += 2; // app id
    if i + 7 > reply.len() {
        return None;
    }
    let (players, max, bots) = (reply[i] as i32, reply[i + 1] as i32, reply[i + 2] as i32);
    Some(ServerInfo { map, players: (players - bots).max(0), max_players: max, bots, password: reply[i + 5] == 1, ping: 0 })
}

// -------------------------------------------------------------------------------------------------
// Invites: the address friends use, one-line codes ("LST-…") and reading whatever a friend pastes
// -------------------------------------------------------------------------------------------------

pub const DOWNLOAD_URL: &str = "https://github.com/V3rkku1/Cs-plugin/releases/tag/latest";

/// This PC's address for the current friends mode, or None when there isn't one yet.
pub fn host_address(s: &ServerSettings) -> Option<String> {
    if s.friends == server::INTERNET {
        return ROUTER.lock().unwrap().external.map(|e| e.to_string());
    }
    let vpn = s.friends == server::VPN;
    addresses().into_iter().find(|a| a.is_vpn() == vpn).map(|a| a.address.to_string())
}

fn with_port(address: &str) -> String {
    if address.contains(':') { address.into() } else { format!("{address}:{PORT}") }
}

pub fn invite_code(address: &str, password: &str) -> String {
    format!("LST-{}", base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(format!("{}|{password}", with_port(address))))
}

pub fn invite_text(s: &ServerSettings, address: &str) -> String {
    let mut lines = vec![
        "Join my CS2 server!".to_string(),
        format!("1. Get LAN Skin Tester: {DOWNLOAD_URL}"),
        "2. Friends → Join a Friend → paste this code:".into(),
        format!("   {}", invite_code(address, &s.password)),
        format!("(or type the address {address}{}", if s.password.is_empty() { ")".into() } else { format!(" and password {})", s.password) }),
    ];
    if s.friends == server::VPN {
        lines.insert(1, "0. Be in the same Radmin VPN network as me".into());
    }
    if !s.shared_skins {
        lines.push("Pick skins in chat: !skin <name>, !knife <name>, !gen <code>".into());
    }
    lines.join("\r\n")
}

/// Reads an invite code, a whole invite message, "connect 1.2.3.4:27015; password x", steam://connect/… or a
/// plain address. The password box is used when the text has none.
pub fn parse_invite(text: &str, fallback_password: &str) -> Option<JoinTarget> {
    static CODE: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"LST-([A-Za-z0-9_\-]+)").unwrap());
    static STEAM: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(?i)steam://(?:run/730//\+)?connect/([^/\s]+)(?:/([^\s]*))?").unwrap());
    static PASSWORD: LazyLock<Regex> = LazyLock::new(|| Regex::new(r#"(?i)password\s+"?([^";\s]+)"#).unwrap());
    static ADDRESS: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(?i)(\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?|[a-z0-9][a-z0-9\-\.]*\.[a-z]{2,}(?::\d{1,5})?)").unwrap());
    let text = text.trim();
    if text.is_empty() {
        return None;
    }
    if let Some(target) = CODE.captures(text).and_then(|c| decode_code(&c[1])) {
        return Some(target);
    }
    if let Some(c) = STEAM.captures(text) {
        return Some(JoinTarget { address: with_port(&c[1]), password: crate::util::unescape(c.get(2).map_or("", |m| m.as_str())) });
    }
    let address = ADDRESS.captures(text)?[1].to_string();
    let mut pass = match PASSWORD.captures(text) {
        Some(p) => p[1].to_string(),
        None => fallback_password.trim().to_string(),
    };
    // The invite's "(or type the address … and password X)" line: the bracket isn't part of the password.
    if PASSWORD.is_match(text) && pass.ends_with(')') && !pass.contains('(') && text.contains('(') {
        pass.pop();
    }
    Some(JoinTarget { address: with_port(&address), password: pass })
}

fn decode_code(body: &str) -> Option<JoinTarget> {
    let bytes = base64::engine::general_purpose::URL_SAFE_NO_PAD.decode(body.trim_end_matches('=')).ok()?;
    let text = String::from_utf8(bytes).ok()?;
    let (address, password) = text.split_once('|').unwrap_or((&text, ""));
    (!address.is_empty()).then(|| JoinTarget { address: with_port(address), password: password.into() })
}

/// Makes an already running CS2 connect (Steam hands the link to the open game).
pub fn steam_connect_url(t: &JoinTarget) -> String {
    let pass = if t.password.is_empty() { String::new() } else { format!("/{}", crate::util::escape(&t.password)) };
    format!("steam://connect/{}{pass}", t.address)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn target(a: &str, p: &str) -> Option<JoinTarget> {
        Some(JoinTarget { address: a.into(), password: p.into() })
    }

    #[test]
    fn invites() {
        assert_eq!(parse_invite("(or type the address 1.2.3.4 and password abc)", ""), target("1.2.3.4:27015", "abc"));
        assert_eq!(parse_invite("connect 1.2.3.4:27016; password x)y", ""), target("1.2.3.4:27016", "x)y"));
        assert_eq!(parse_invite(&invite_code("5.6.7.8", "pw"), ""), target("5.6.7.8:27015", "pw"));
        assert_eq!(parse_invite("steam://connect/9.9.9.9:27015/a%20b", ""), target("9.9.9.9:27015", "a b"));
        assert_eq!(parse_invite("nothing here", ""), None);
    }

    #[test]
    fn addresses_by_network() {
        assert_eq!(classify(Ipv4Addr::new(26, 1, 2, 3), "Ethernet"), Some("Radmin VPN"));
        assert_eq!(classify(Ipv4Addr::new(192, 168, 1, 5), "Wi-Fi"), Some("Wi-Fi / LAN"));
        assert_eq!(classify(Ipv4Addr::new(8, 8, 8, 8), "Wi-Fi"), None);
        assert!(behind_another_nat(Ipv4Addr::new(100, 70, 1, 1)) && !behind_another_nat(Ipv4Addr::new(85, 1, 1, 1)));
    }
}
