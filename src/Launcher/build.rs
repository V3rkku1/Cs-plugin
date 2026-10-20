use std::{env, fs, path::PathBuf};

fn main() {
    // The built plugin ships inside the exe when build.sh says where it is (LST_PLUGIN_DIR).
    println!("cargo:rerun-if-env-changed=LST_PLUGIN_DIR");
    println!("cargo:rerun-if-env-changed=LST_COMMIT");
    let mut code = String::from("pub static PLUGIN_FILES: &[(&str, &[u8])] = &[\n");
    if let Ok(dir) = env::var("LST_PLUGIN_DIR") {
        for file in ["LanSkinTester.dll", "gamedata/lanskintester.json"] {
            let path = PathBuf::from(&dir).join(file);
            println!("cargo:rerun-if-changed={}", path.display());
            code += &format!(
                "    ({file:?}, include_bytes!({:?})),\n",
                path.display().to_string()
            );
        }
    }
    code += "];\n";
    fs::write(
        PathBuf::from(env::var("OUT_DIR").unwrap()).join("plugin_files.rs"),
        code,
    )
    .unwrap();
    tauri_build::build();
}
