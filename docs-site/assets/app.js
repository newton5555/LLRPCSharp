(function () {
  const isEnglish = document.documentElement.lang === "en" || /\/en(?:\/|$)/.test(location.pathname);
  const page = location.pathname.endsWith("/")
    ? "index.html"
    : (location.pathname.split("/").pop() || "index.html");
  const menu = document.querySelector(".menu-toggle");
  const nav = document.querySelector(".topbar nav");
  if (menu && nav) {
    menu.setAttribute("aria-label", isEnglish ? "Toggle navigation" : "切换导航");
    menu.setAttribute("aria-expanded", "false");
    menu.addEventListener("click", () => {
      const open = nav.classList.toggle("open");
      menu.setAttribute("aria-expanded", String(open));
    });
  }

  const languageToggle = document.createElement("a");
  languageToggle.className = "language-toggle";
  languageToggle.href = `${isEnglish ? "../" : "en/"}${page}${location.hash}`;
  languageToggle.textContent = isEnglish ? "中文" : "EN";
  languageToggle.setAttribute("aria-label", isEnglish ? "切换到中文" : "Switch to English");
  languageToggle.title = isEnglish ? "切换到中文" : "Switch to English";
  const languageHost = document.querySelector(".topbar")
    || document.querySelector(".reference-topbar .topbar-links")
    || document.querySelector(".reference-topbar");
  const github = languageHost?.querySelector(".github");
  languageHost?.insertBefore(languageToggle, github || null);

  // Dropdown interactivity
  document.querySelectorAll(".dropdown-toggle").forEach(toggle => {
    toggle.addEventListener("click", (e) => {
      e.stopPropagation();
      const parent = toggle.closest(".nav-dropdown");
      const wasOpen = parent.classList.contains("open");
      document.querySelectorAll(".nav-dropdown.open").forEach(d => d.classList.remove("open"));
      if (!wasOpen) {
        parent.classList.add("open");
        toggle.setAttribute("aria-expanded", "true");
      } else {
        toggle.setAttribute("aria-expanded", "false");
      }
    });
  });

  document.addEventListener("click", () => {
    document.querySelectorAll(".nav-dropdown.open").forEach(d => {
      d.classList.remove("open");
      d.querySelector(".dropdown-toggle")?.setAttribute("aria-expanded", "false");
    });
  });

  // Active link highlighting
  document.querySelectorAll(".topbar nav a, .dropdown-item").forEach(link => {
    const href = link.getAttribute("href");
    if (href && href.split("#")[0] === page) {
      link.classList.add("active");
      const parentDropdown = link.closest(".nav-dropdown");
      if (parentDropdown) {
        parentDropdown.querySelector(".dropdown-toggle")?.classList.add("active");
      }
    }
  });

  document.querySelectorAll(".copy-code").forEach(button => {
    button.addEventListener("click", async () => {
      const code = button.parentElement.querySelector("code").innerText;
      try {
        await navigator.clipboard.writeText(code);
        const old = button.innerText;
        button.innerText = isEnglish ? "Copied" : "已复制";
        setTimeout(() => button.innerText = old, 1300);
      } catch (_) {
        button.innerText = isEnglish ? "Copy manually" : "请手动复制";
      }
    });
  });
})();
