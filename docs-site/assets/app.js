(function () {
  const menu = document.querySelector(".menu-toggle");
  const nav = document.querySelector(".topbar nav");
  if (menu && nav) menu.addEventListener("click", () => nav.classList.toggle("open"));

  const page = location.pathname.split("/").pop() || "index.html";
  document.querySelectorAll(".topbar nav a").forEach(link => {
    if (link.getAttribute("href").split("#")[0] === page) link.classList.add("active");
  });

  document.querySelectorAll(".copy-code").forEach(button => {
    button.addEventListener("click", async () => {
      const code = button.parentElement.querySelector("code").innerText;
      try {
        await navigator.clipboard.writeText(code);
        const old = button.innerText;
        button.innerText = "已复制";
        setTimeout(() => button.innerText = old, 1300);
      } catch (_) {
        button.innerText = "请手动复制";
      }
    });
  });
})();
