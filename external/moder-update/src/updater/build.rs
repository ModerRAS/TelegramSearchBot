use std::{env, path::Path};

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=updater.exe.manifest");

    if env::var_os("CARGO_CFG_WINDOWS").is_none() {
        return;
    }

    let target_env = env::var("CARGO_CFG_TARGET_ENV").unwrap_or_default();
    if target_env != "msvc" {
        return;
    }

    let manifest_dir = env::var("CARGO_MANIFEST_DIR")
        .expect("Cargo should set CARGO_MANIFEST_DIR for build scripts.");
    let manifest_path = Path::new(&manifest_dir).join("updater.exe.manifest");

    println!("cargo:rustc-link-arg-bin=moder_update_updater=/MANIFEST:EMBED");
    println!(
        "cargo:rustc-link-arg-bin=moder_update_updater=/MANIFESTINPUT:{}",
        manifest_path.display()
    );
}
