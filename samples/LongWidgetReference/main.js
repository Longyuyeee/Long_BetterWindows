const fallback = {
  "page.title": "Long Widget Reference",
  "page.description": "一个可安装、可打包、可测试的 Web Widget 参考实现。"
};

function applyLocalization(message) {
  const resources = message?.resources ?? fallback;
  document.documentElement.lang = message?.resolved_language ?? "zh-CN";
  document.querySelectorAll("[data-i18n]").forEach(element => {
    const key = element.dataset.i18n;
    element.textContent = resources[key] ?? element.textContent;
  });
}

window.chrome?.webview?.addEventListener("message", ({ data }) => {
  if (data?.type === "long.language-changed") applyLocalization(data);
});
