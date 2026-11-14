if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("service-worker.js", { updateViaCache: "none" });
}
window.cmr = {
  get: (key) => localStorage.getItem(key),
  set: (key, value) => localStorage.setItem(key, value)
};
