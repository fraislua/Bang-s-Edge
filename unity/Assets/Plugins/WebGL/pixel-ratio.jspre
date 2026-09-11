// Bang's-Edge: draw at the phone's real pixel density on unityroom.
// unityroom's play page sets config.devicePixelRatio = 1 for iPhone/iPad/Android user agents, so
// the canvas is rendered at its CSS size and stretched by the browser: on a high-density phone held
// sideways the board came out visibly soft. Unity reads Module.devicePixelRatio whenever it sizes
// the canvas (_JS_SystemInfo_GetPreferredDevicePixelRatio), and this runs after the page's config
// has been copied into Module, so setting it here wins. Only a ratio the page forced is replaced;
// without one Unity already follows window.devicePixelRatio (desktop, our own page template).
// The cap only trims unusually dense screens: the real-device test on our own page template, which
// already drew at the phone's full ratio, looked sharp and played fine. Lower it if phones slow down.
(function () {
  var MAX_PIXEL_RATIO = 3;
  if (typeof Module === 'undefined' || typeof window === 'undefined') return;
  if (Module.devicePixelRatio === undefined) return;
  Module.devicePixelRatio = Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO);
})();
