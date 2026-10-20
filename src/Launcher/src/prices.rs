//! Real market prices for the Cases and Inventory pages: Skinport's public price list (one request, refreshed
//! every 12 hours, cached on disk), and the Steam Community Market for items it doesn't have. For showing only.

use crate::util;
use serde_json::Value;
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime};

pub const CURRENCIES: [&str; 3] = ["USD", "EUR", "GBP"];
const WEAR_ORDER: [&str; 5] = ["Factory New", "Minimal Wear", "Field-Tested", "Well-Worn", "Battle-Scarred"];

#[derive(Default)]
struct Inner {
    currency: String,
    /// Lower-case market name -> price.
    prices: HashMap<String, f64>,
    /// "ak-47 | redline" -> its prices by what's in the brackets ("Field-Tested"...).
    variants: HashMap<String, Vec<(String, f64)>>,
    steam: HashMap<String, Option<f64>>,
    loaded_at: Option<SystemTime>,
    loading: bool,
    last_notify: Option<Instant>,
}

pub struct PriceList {
    inner: Arc<Mutex<Inner>>,
    steam_gate: Arc<Mutex<()>>,
    http: ureq::Agent,
    log: Arc<dyn Fn(String) + Send + Sync>,
    changed: Arc<dyn Fn() + Send + Sync>,
}

/// "Name (Variant)" split in two; Variant is None when there are no brackets.
fn split(name: &str) -> (&str, Option<&str>) {
    match name.rfind(" (") {
        Some(open) if open > 0 && name.ends_with(')') => (&name[..open], Some(&name[open + 2..name.len() - 1])),
        _ => (name, None),
    }
}

fn cache_file(currency: &str) -> PathBuf {
    crate::itemdb::data_dir().join(format!("prices-{}.json", currency.to_lowercase()))
}

impl Inner {
    fn set_prices(&mut self, prices: HashMap<String, f64>) {
        self.variants.clear();
        for (name, price) in &prices {
            if let (base, Some(variant)) = split(name) {
                self.variants.entry(base.to_lowercase()).or_default().push((variant.to_string(), *price));
            }
        }
        self.prices = prices.into_iter().map(|(k, v)| (k.to_lowercase(), v)).collect();
    }
}

impl PriceList {
    pub fn new(log: Arc<dyn Fn(String) + Send + Sync>, changed: Arc<dyn Fn() + Send + Sync>) -> Self {
        Self { inner: Default::default(), steam_gate: Default::default(), http: util::http_agent(60), log, changed }
    }

    fn notify(inner: &Mutex<Inner>, changed: &(dyn Fn() + Send + Sync)) {
        let mut i = inner.lock().unwrap();
        if i.last_notify.is_some_and(|t| t.elapsed() < Duration::from_secs(3)) {
            return;
        }
        i.last_notify = Some(Instant::now());
        drop(i);
        changed();
    }

    /// Loads the cached list, and downloads a new one in the background when it is old.
    pub fn ensure(&self, currency: &str) {
        let currency = if CURRENCIES.contains(&currency) { currency } else { "USD" };
        let mut i = self.inner.lock().unwrap();
        if i.currency != currency {
            i.currency = currency.into();
            let file = cache_file(currency);
            let cached: HashMap<String, f64> = fs::read_to_string(&file).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or_default();
            i.loaded_at = if cached.is_empty() { None } else { fs::metadata(&file).and_then(|m| m.modified()).ok() };
            i.set_prices(cached);
            i.steam.clear();
        }
        let old = i.loaded_at.map_or(true, |t| t.elapsed().unwrap_or_default() > Duration::from_secs(12 * 3600));
        if old && !i.loading {
            i.loading = true;
            let (inner, http, log, changed, currency) = (self.inner.clone(), self.http.clone(), self.log.clone(), self.changed.clone(), currency.to_string());
            std::thread::spawn(move || {
                let result = download(&http, &currency);
                let mut i = inner.lock().unwrap();
                i.loading = false;
                match result {
                    Ok(prices) => {
                        let count = prices.len();
                        let _ = fs::create_dir_all(crate::itemdb::data_dir());
                        let _ = fs::write(cache_file(&currency), serde_json::to_string(&prices).unwrap());
                        if i.currency == currency {
                            i.set_prices(prices);
                            i.loaded_at = Some(SystemTime::now());
                        }
                        drop(i);
                        log(format!("Prices updated: {count} items ({currency})."));
                        Self::notify(&inner, &*changed);
                    }
                    Err(e) => {
                        // Try again in an hour.
                        i.loaded_at = Some(SystemTime::now() - Duration::from_secs(11 * 3600));
                        drop(i);
                        log(format!("Could not download prices (they show when online): {e}"));
                    }
                }
            });
        }
    }

    /// The price of a market name. Without an exact price, the nearest wear or the cheapest variant; only when
    /// nothing at all is known is Steam asked (in the background; the page refreshes when it answers).
    pub fn price(&self, market_name: &str) -> Option<f64> {
        let mut i = self.inner.lock().unwrap();
        let key = market_name.to_lowercase();
        if let Some(p) = i.prices.get(&key) {
            return Some(*p);
        }
        let (base, wanted) = split(market_name);
        if let Some(variants) = i.variants.get(&base.to_lowercase()).filter(|v| !v.is_empty()) {
            if let Some(at) = wanted.and_then(|w| WEAR_ORDER.iter().position(|x| *x == w)) {
                let nearest = variants.iter()
                    .filter_map(|(v, p)| WEAR_ORDER.iter().position(|x| x == v).map(|o| (o.abs_diff(at), *p)))
                    .min_by(|a, b| a.0.cmp(&b.0).then(a.1.total_cmp(&b.1)));
                if let Some((_, p)) = nearest {
                    return Some(p);
                }
            }
            return variants.iter().map(|v| v.1).min_by(f64::total_cmp);
        }
        if let Some(known) = i.steam.get(&key) {
            return *known;
        }
        i.steam.insert(key.clone(), None); // asked once per run
        let currency_id = match i.currency.as_str() { "EUR" => 3, "GBP" => 2, _ => 1 };
        drop(i);
        let (inner, gate, http, changed, name) = (self.inner.clone(), self.steam_gate.clone(), self.http.clone(), self.changed.clone(), market_name.to_string());
        std::thread::spawn(move || {
            // Steam rate-limits: one request at a time, spaced out.
            let _turn = gate.lock().unwrap();
            let url = format!("https://steamcommunity.com/market/priceoverview/?appid=730&currency={currency_id}&market_hash_name={}", util::escape(&name));
            let doc: Option<Value> = util::get_text(&http, &url).ok().and_then(|t| serde_json::from_str(&t).ok());
            let text = doc.as_ref().and_then(|d| d["median_price"].as_str().or(d["lowest_price"].as_str()));
            if let Some(price) = parse_steam_price(text.unwrap_or("")) {
                inner.lock().unwrap().steam.insert(key, Some(price));
                Self::notify(&inner, &*changed);
            }
            std::thread::sleep(Duration::from_millis(3500));
        });
        None
    }
}

fn download(http: &ureq::Agent, currency: &str) -> Result<HashMap<String, f64>, String> {
    let url = format!("https://api.skinport.com/v1/items?app_id=730&currency={currency}&tradable=0");
    let mut r = http.get(&url)
        .header("User-Agent", "LanSkinTester/2 (+https://github.com/V3rkku1/Cs-plugin)")
        .header("Accept", "application/json")
        .call().map_err(|e| e.to_string())?;
    if !r.status().is_success() {
        return Err(format!("HTTP {}", r.status()));
    }
    let doc: Value = serde_json::from_str(&r.body_mut().with_config().limit(256 << 20).read_to_string().map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    let mut prices = HashMap::new();
    for item in doc.as_array().into_iter().flatten() {
        let Some(name) = item["market_hash_name"].as_str() else { continue };
        // Suggested is Skinport's market estimate; the cheapest listing when there is none.
        if let Some(p) = ["suggested_price", "median_price", "min_price"].iter().find_map(|f| item[f].as_f64()).filter(|p| *p > 0.0) {
            prices.insert(name.to_string(), (p * 100.0).round() / 100.0);
        }
    }
    if prices.is_empty() { Err("no prices in the answer".into()) } else { Ok(prices) }
}

/// "1.234,56€" or "$1,234.56": the last separator is the decimal point.
pub fn parse_steam_price(text: &str) -> Option<f64> {
    let mut digits: String = text.chars().filter(|c| c.is_ascii_digit() || *c == ',' || *c == '.').collect::<String>().replace(',', ".");
    if let Some(last) = digits.rfind('.').filter(|&l| l > 0) {
        digits = digits[..last].replace('.', "") + &digits[last..];
    }
    digits.parse::<f64>().ok().filter(|p| *p > 0.0)
}

pub fn wear_name(wear: f32) -> &'static str {
    match wear {
        w if w < 0.07 => "Factory New",
        w if w < 0.15 => "Minimal Wear",
        w if w < 0.38 => "Field-Tested",
        w if w < 0.45 => "Well-Worn",
        _ => "Battle-Scarred",
    }
}

/// The Steam market name: "StatTrak™ AK-47 | Redline (Field-Tested)", "★ StatTrak™ Karambit | Fade (Factory New)",
/// "Souvenir AWP | Dragon Lore (Minimal Wear)", "★ Karambit", "Sticker | ...".
pub fn market_name(name: &str, skin: bool, has_paint: bool, wear: f32, stat_trak: bool, souvenir: bool) -> String {
    let name = name.replace("StatTrak™ ", "");
    let name = name.trim();
    let star = name.starts_with('★');
    let base = if star { name.trim_start_matches('★').trim() } else { name };
    if !skin {
        return format!("{}{base}", if stat_trak && !star { "StatTrak™ " } else { "" });
    }
    let prefix = format!("{}{}{}", if star { "★ " } else { "" }, if stat_trak { "StatTrak™ " } else { "" }, if souvenir && !star { "Souvenir " } else { "" });
    format!("{prefix}{base}{}", if has_paint { format!(" ({})", wear_name(wear)) } else { String::new() })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn steam_prices() {
        for (text, want) in [("$1,234.56", 1234.56), ("1.234,56€", 1234.56), ("0,03€", 0.03), ("£12.50", 12.5)] {
            assert!((parse_steam_price(text).unwrap() - want).abs() < 1e-6, "{text}");
        }
        assert_eq!(parse_steam_price("--"), None);
    }

    #[test]
    fn market_names() {
        assert_eq!(market_name("AK-47 | Redline", true, true, 0.2, true, false), "StatTrak™ AK-47 | Redline (Field-Tested)");
        assert_eq!(market_name("★ Karambit | Fade", true, true, 0.01, true, true), "★ StatTrak™ Karambit | Fade (Factory New)");
        assert_eq!(market_name("★ Karambit", true, false, 0.0, false, false), "★ Karambit");
    }
}
