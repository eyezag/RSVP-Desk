// Confirmation for forms marked data-confirm (kept out of inline handlers so the CSP can forbid inline script).
document.addEventListener("submit", (e) => {
  const form = e.target;
  if (form instanceof HTMLFormElement && form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
    e.preventDefault();
  }
});
