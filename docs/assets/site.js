(() => {
  "use strict";
  const REPO = "Haguilar91/OpenCleaner";
  const RELEASES = `https://github.com/${REPO}/releases/latest`;

  // ---------- Spanish strings (English lives in the HTML) ----------
  const ES = {
    skip: "Ir al contenido",
    nav_features: "Funciones", nav_install: "Instalar", nav_safety: "Seguridad", nav_faq: "Preguntas",
    hero_h: "Libera espacio. Mira exactamente qué se borra.",
    hero_p: "OpenCleaner es un limpiador de disco gratis y de código abierto para Windows, macOS, Linux, Android y Android TV. Te muestra lo que encontró, con tamaños, y solo borra lo que marcas y confirmas.",
    hero_dl: "Descargar", hero_all: "Otras plataformas",
    hero_fine: "Última versión:", hero_mit: "Licencia MIT.",
    tm_photos: "Fotos", tm_projects: "Proyectos", tm_backup: "respaldo.zip", tm_music: "Música",
    watch_h: "OpenCleaner en menos de un minuto",
    feat_h: "Qué hace",
    f1_h: "Cada categoría tiene su fila",
    f1_p: "OpenCleaner busca espacio recuperable y muestra cada categoría con su tamaño. Si no hay nada que borrar, la fila sigue ahí y dice Limpio, así ves qué se revisó.",
    f1_p2: "Nada se marca a ciegas. Tú marcas qué se borra, revisas los archivos de cada elemento si quieres, y confirmas.",
    f2_h: "Encuentra qué ocupa espacio de verdad",
    f2_p: "Uso del disco muestra tus carpetas como lista o como un mapa de bloques de colores según su tamaño. Haz clic en una carpeta para entrar o en un archivo para verlo: imágenes, texto, detalles y un reproductor integrado para video y audio.",
    f2_p2: "Ordena por tamaño, antigüedad o tipo, muestra solo el Top 5, 10, 50 o 100, y selecciona varios elementos para borrarlos juntos.",
    f3_h: "Encuentra apps que no usas",
    f3_p: "La página Apps muestra cada app instalada con su tamaño y cuándo la abriste por última vez. En computadoras, Desinstalar te enseña primero la app y sus restos, como cachés y preferencias.",
    f3_warn: "Desinstalar es permanente. Nada va a la Papelera, y lo que pueda tener tus datos empieza sin marcar.",
    cleans_h: "Qué limpia cada versión",
    cl_win: "Carpetas temporales, caché de Windows Update, volcados de errores, Papelera de reciclaje, cachés de navegadores y de desarrollo, y Steam.",
    cl_mac: "Homebrew, registros, cachés de apps, Papelera, datos de Xcode, simuladores, respaldos de iOS y Steam.",
    cl_lin: "La caché y los paquetes sin uso de tu distro (pacman, APT, DNF), revisiones viejas de Snap, el journal de systemd, runtimes de Flatpak sin uso, carpetas de caché grandes, Papelera y prefijos de Proton de Steam que sobran.",
    cl_and: "Su propia caché, miniaturas, APKs viejos, copias enviadas de WhatsApp, archivos grandes y el tamaño de cada app, dentro de lo que Android permite sin root.",
    inst_h: "Descarga e instala",
    inst_p: "Elige tu sistema. Cada guía tiene un video corto y los mismos pasos por escrito.",
    win_req: "Windows 10 u 11.",
    win_s1: "Descarga el instalador", win_s1p: "La mayoría de PCs usa x64. Usa arm64 para laptops ARM y x86 para Windows de 32 bits.",
    win_s2: "Pasa SmartScreen", win_s2p: "El instalador aún no está firmado, así que Windows avisa de un editor desconocido. Elige Más información y luego Ejecutar de todas formas.",
    win_s3: "Instala", win_s3p: "Se instala solo para ti, sin contraseña de administrador. Puedes crear un acceso directo en el escritorio.",
    win_s4: "Abre OpenCleaner", win_s4p: "Búscalo en el menú Inicio. ¿Prefieres no instalar? El .exe portable funciona por sí solo.",
    dl_x64: "Instalador x64", dl_arm64: "Instalador arm64", dl_x86: "Instalador x86", dl_portable: ".exe portable",
    mac_req: "macOS 13 Ventura o posterior, en Apple silicon o Intel.",
    mac_s1: "Descarga el zip", mac_s1p: "Una sola descarga funciona en cualquier Mac.",
    mac_s2: "Muévela a Aplicaciones", mac_s2p: "Haz doble clic en el zip para abrirlo y arrastra OpenCleaner a Aplicaciones.",
    mac_s3: "Ábrela la primera vez", mac_s3p: "La app aún no está notarizada, así que macOS pregunta antes. Haz clic derecho en la app, elige Abrir y luego Abrir otra vez.",
    mac_s4: "¿No aparece Abrir?", mac_s4p: "En macOS 15 Sequoia o posterior, ve a Ajustes del Sistema › Privacidad y seguridad y haz clic en Abrir igualmente. Solo se hace una vez.",
    dl_mac: "Descargar para Mac",
    lin_req: "Un solo archivo autónomo. Funciona en Arch, CachyOS, Debian, Ubuntu, Mint, Fedora, Bazzite y más.",
    lin_s1: "Descarga la app", lin_s1p: "Usa x86_64 para la mayoría de PCs y arm64 para laptops ARM y Raspberry Pi OS de 64 bits.",
    lin_s2: "Hazlo ejecutable", lin_s2p: "En una terminal, en tu carpeta de Descargas: <code>chmod +x opencleaner-*-linux-x86_64</code>",
    lin_s3: "Ejecútalo", lin_s3p: "Ejecuta <code>./opencleaner-*-linux-x86_64</code>. No hace falta instalar; guarda el archivo donde quieras.",
    lin_s4: "¿Prefieres usar el mouse?", lin_s4p: "En tu explorador de archivos, abre las Propiedades del archivo y permite ejecutarlo como programa; luego haz doble clic. El nombre exacto depende de tu escritorio.",
    and_req: "Android 11 o posterior.",
    and_s1: "Descarga el APK", and_s1p: "Abre esta página en tu teléfono y toca el botón de descarga.",
    and_s2: "Permite la instalación", and_s2p: "Android pregunta antes de instalar apps de fuera de Play Store. Permítelo solo para tu navegador.",
    and_s3: "Instala", and_s3p: "Abre el APK y toca Instalar. Si Play Protect pregunta, sigue su aviso.",
    and_s4: "Dale acceso", and_s4p: "OpenCleaner necesita acceso a todos los archivos para buscar espacio, y acceso al uso para la página Apps. Te lleva a cada ajuste.",
    dl_apk: "Descargar el APK",
    tv_req: "Google TV, Android TV 9 o posterior, y Fire TV. Elige cómo enviarás la app a tu TV:",
    tv_r1: "Con LocalSend", tv_r2: "Con adb inalámbrico",
    tvl_s1: "Instala LocalSend", tvl_s1p: "Instala la app gratis <a href=\"https://localsend.org\">LocalSend</a> en tu TV y en la computadora o teléfono desde el que enviarás.",
    tvl_s2: "Envía el APK", tvl_s2p: "Baja el APK de Android TV y envíalo a tu TV con LocalSend. Ambos equipos deben estar en el mismo Wi-Fi.",
    tvl_s3: "Acéptalo en el TV", tvl_s3p: "LocalSend en el TV pregunta antes de recibir. Elige Aceptar.",
    tvl_s4: "Instálala", tvl_s4p: "Abre el APK en LocalSend. La primera vez, permite que LocalSend instale apps.",
    tvl_s5: "Ábrela con el control", tvl_s5p: "Si pide un acceso para el que tu TV no tiene pantalla, te muestra los comandos adb. Mira los pasos de adb inalámbrico.",
    tva_s1: "Activa el modo desarrollador", tva_s1p: "En Ajustes › Sistema › Acerca de, selecciona la compilación 7 veces. En Fire TV: Mi Fire TV › Acerca de, selecciona el nombre del dispositivo 7 veces.",
    tva_s2: "Activa la depuración", tva_s2p: "En Opciones de desarrollador, activa la depuración USB (depuración ADB en Fire TV, depuración de red en NVIDIA Shield). Anota la IP del TV en Ajustes › Red.",
    tva_s3: "Conéctate por Wi-Fi", tva_s3p: "En tu computadora, ejecuta <code>adb connect 192.168.1.50</code> con la IP de tu TV y permite la depuración en el TV. Si tu TV usa depuración inalámbrica (Android 11+), ejecuta antes <code>adb pair</code> con su código.",
    tva_s4: "Instala el APK",
    tva_s5: "Da acceso si lo pide", tva_s5p: "Si el TV no tiene pantalla para un permiso, ejecuta:<br><code>adb shell appops set com.opencleaner.tv MANAGE_EXTERNAL_STORAGE allow</code><br><code>adb shell appops set com.opencleaner.tv GET_USAGE_STATS allow</code>",
    dl_tv: "Descargar el APK de Android TV",
    sums: "Cada versión incluye", sums2: "Desde la 1.3, las apps se actualizan solas y verifican cada descarga con ese archivo.",
    safe_h: "Hecha para que tú tengas el control",
    safe_p: "Un limpiador borra cosas, así que OpenCleaner es estricto con lo que toca.",
    safe_1: "Nada se borra hasta que lo marcas y confirmas.",
    safe_2: "Cada elemento solo puede borrar dentro de sus propias carpetas. Todo lo demás se rechaza.",
    safe_3: "Los archivos en uso se saltan, nunca se fuerzan.",
    safe_4: "Al borrar en Uso del disco, todo va a la Papelera por defecto. El borrado permanente es un ajuste, y la confirmación siempre dice cuál aplica.",
    safe_5: "Partidas guardadas, respaldos de dispositivos, paquetes huérfanos y cachés de apps abiertas empiezan sin marcar.",
    safe_6: "Las actualizaciones se verifican con el SHA256SUMS.txt de la versión antes de instalarse.",
    faq_h: "Preguntas",
    q1: "¿OpenCleaner es gratis?", a1: "Sí. Es gratis y de código abierto con licencia MIT, sin anuncios ni cuentas.",
    q2: "¿Envía algún dato?", a2: "Solo una consulta a GitHub al abrir la app, para ver si hay una versión nueva. No se envía nada sobre ti ni sobre tus archivos.",
    q3: "¿Por qué Windows y macOS muestran un aviso?", a3: "El instalador de Windows aún no está firmado y la app de Mac aún no está notarizada. Esos avisos salen con cualquier app que no lo esté. Las guías muestran cómo pasar cada uno.",
    q4: "¿Puedo deshacer un borrado?", a4: "En Uso del disco, lo borrado va a la Papelera por defecto, así que puedes restaurarlo. Los borrados permanentes, la limpieza y las desinstalaciones no se pueden deshacer, y la confirmación lo dice. Android no tiene papelera, así que ahí siempre es permanente.",
    q5: "¿Qué descarga de Windows necesito?", a5: "La mayoría de PCs usa el instalador x64. Usa arm64 para laptops con Windows en ARM, y x86 solo para Windows 10 de 32 bits. El .exe portable funciona sin instalar.",
    q6: "¿Está terminada?", a6: "Está en desarrollo activo. Algunas funciones nuevas están escritas pero aún no se han probado en todas las plataformas; la <a href=\"https://github.com/Haguilar91/OpenCleaner#status\">tabla de estado</a> dice exactamente cuáles. Lee la lista de confirmación antes de limpiar y reporta errores en GitHub.",
    foot: "OpenCleaner es de código abierto con licencia MIT.", foot_gh: "Código y versiones en GitHub",
    dl_for: "Descargar para ", title: "OpenCleaner: limpiador de disco gratis y de código abierto"
  };
  const EN_EXTRA = { dl_for: "Download for ", title: document.title };

  // ---------- language ----------
  const nodes = [...document.querySelectorAll("[data-i18n],[data-i18n-html]")];
  nodes.forEach(n => { n.dataset.en = n.innerHTML; });
  let lang = "en";
  function pickLang() {
    const q = new URLSearchParams(location.search).get("lang");
    if (q === "es" || q === "en") return q;
    try { const s = localStorage.getItem("oc-lang"); if (s) return s; } catch (e) {}
    return (navigator.language || "").toLowerCase().startsWith("es") ? "es" : "en";
  }
  function t(key) { return lang === "es" ? ES[key] : EN_EXTRA[key]; }
  function setLang(l) {
    lang = l;
    document.documentElement.lang = l;
    nodes.forEach(n => {
      const key = n.dataset.i18n || n.dataset.i18nHtml;
      n.innerHTML = l === "es" && ES[key] ? ES[key] : n.dataset.en;
    });
    document.title = t("title");
    const btn = document.getElementById("lang");
    btn.textContent = l === "es" ? "English" : "Español";
    btn.setAttribute("aria-label", l === "es" ? "Switch to English" : "Cambiar a español");
    document.querySelectorAll("video[data-video]").forEach(v => {
      const base = v.dataset.video;
      const src = `videos/${base}-${l}.mp4`;
      if (!v.getAttribute("src") || !v.getAttribute("src").endsWith(src)) {
        v.pause(); v.setAttribute("poster", `videos/${base}-${l}.jpg`); v.setAttribute("src", src);
      }
    });
    heroLabel();
    try { localStorage.setItem("oc-lang", l); } catch (e) {}
  }
  document.getElementById("lang").addEventListener("click", () => setLang(lang === "es" ? "en" : "es"));

  // ---------- platform detection ----------
  function detectOS() {
    const ua = navigator.userAgent || "";
    if (/Android/i.test(ua)) return /TV|AFT|BRAVIA|SMART-TV/i.test(ua) ? "tv" : "android";
    if (/Windows/i.test(ua)) return "windows";
    if (/Mac OS X|Macintosh/i.test(ua)) return /iPhone|iPad/i.test(ua) ? null : "macos";
    if (/Linux|X11/i.test(ua)) return "linux";
    return null;
  }
  const OS = detectOS();
  const OS_ASSET = { windows: "win_x64", macos: "mac", linux: "linux_x64", android: "android", tv: "tv" };
  const OS_NAME = { windows: "Windows", macos: "Mac", linux: "Linux", android: "Android", tv: "Android TV" };
  function heroLabel() {
    const b = document.getElementById("hero-dl");
    if (OS) b.textContent = t("dl_for") + OS_NAME[OS];
  }

  // ---------- tabs ----------
  const tabs = [...document.querySelectorAll('[role="tab"]')];
  function selectTab(tab, focus) {
    tabs.forEach(x => {
      const on = x === tab;
      x.setAttribute("aria-selected", on); x.tabIndex = on ? 0 : -1;
      document.getElementById(x.getAttribute("aria-controls")).hidden = !on;
      if (!on) document.querySelectorAll(`#${x.getAttribute("aria-controls")} video`).forEach(v => v.pause());
    });
    if (focus) tab.focus();
  }
  tabs.forEach((tab, i) => {
    tab.addEventListener("click", () => selectTab(tab));
    tab.addEventListener("keydown", e => {
      const k = { ArrowRight: 1, ArrowLeft: -1 }[e.key];
      if (k) { e.preventDefault(); selectTab(tabs[(i + k + tabs.length) % tabs.length], true); }
      if (e.key === "Home") { e.preventDefault(); selectTab(tabs[0], true); }
      if (e.key === "End") { e.preventDefault(); selectTab(tabs[tabs.length - 1], true); }
    });
  });
  if (OS) selectTab(document.getElementById("t-" + OS));

  // ---------- Android TV route ----------
  document.querySelectorAll("[data-route]").forEach(b => b.addEventListener("click", () => {
    const r = b.dataset.route;
    document.querySelectorAll("[data-route]").forEach(x => { x.classList.toggle("on", x === b); x.setAttribute("aria-pressed", x === b); });
    document.querySelectorAll("[data-route-panel]").forEach(p => { p.hidden = p.dataset.routePanel !== r; });
    const v = document.querySelector("[data-route-video]");
    v.dataset.video = "install-tv-" + r; v.pause();
    v.setAttribute("poster", `videos/install-tv-${r}-${lang}.jpg`); v.setAttribute("src", `videos/install-tv-${r}-${lang}.mp4`);
  }));

  // ---------- latest release from GitHub ----------
  const PATTERNS = {
    win_x64: /Setup-x64\.exe$/i, win_arm64: /Setup-arm64\.exe$/i, win_x86: /Setup-x86\.exe$/i,
    win_portable: /portable\.exe$/i, mac: /macOS\.zip$/i, linux_x64: /linux-x86_64$/i,
    linux_arm64: /linux-arm64$/i, android: /-android\.apk$/i, tv: /-androidtv\.apk$/i, sums: /SHA256SUMS/i
  };
  function mb(bytes) { return bytes >= 1048576 ? (bytes / 1048576).toFixed(0) + " MB" : Math.max(1, Math.round(bytes / 1024)) + " KB"; }
  function applyRelease(rel) {
    if (!rel || !rel.assets) return;
    const ver = (rel.tag_name || "").replace(/^v/i, "");
    if (ver) document.querySelectorAll("[data-version]").forEach(n => { n.textContent = ver; });
    const found = {};
    rel.assets.forEach(a => { for (const k in PATTERNS) if (PATTERNS[k].test(a.name) && !found[k]) found[k] = a; });
    document.querySelectorAll("[data-asset]").forEach(el => {
      const a = found[el.dataset.asset];
      if (!a) return;
      el.href = a.browser_download_url;
      if (el.classList.contains("btn") && !el.querySelector(".size")) {
        const s = document.createElement("span"); s.className = "size"; s.textContent = mb(a.size); el.appendChild(s);
      }
    });
    const hero = document.getElementById("hero-dl");
    if (OS && found[OS_ASSET[OS]]) hero.href = found[OS_ASSET[OS]].browser_download_url;
  }
  function loadRelease() {
    try {
      const c = JSON.parse(sessionStorage.getItem("oc-release") || "null");
      if (c && Date.now() - c.t < 3600e3) { applyRelease(c.r); return; }
    } catch (e) {}
    fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { Accept: "application/vnd.github+json" } })
      .then(r => r.ok ? r.json() : null)
      .then(r => {
        if (!r) return;
        applyRelease(r);
        try { sessionStorage.setItem("oc-release", JSON.stringify({ t: Date.now(), r: { tag_name: r.tag_name, assets: r.assets.map(a => ({ name: a.name, size: a.size, browser_download_url: a.browser_download_url })) } })); } catch (e) {}
      })
      .catch(() => {}); // links already point at the releases page
  }

  setLang(pickLang());
  loadRelease();
})();
