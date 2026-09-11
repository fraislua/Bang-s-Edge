// Bang's-Edge: start Web Audio inside the user's gesture itself.
// Unity reads input on the next frame, outside the browser's event handler, and browsers may only
// let an AudioContext start from inside that handler. The web version calls AudioController.init()
// on every pointerdown (script.js handlePointerDown), so do the same here, directly in the event.
// audio.jspre is a verbatim copy of the web version's audio.js and defines window.AudioController.
(function () {
  document.addEventListener('pointerdown', function () {
    var audio = window.AudioController;
    if (audio && typeof audio.init === 'function') audio.init();
  }, true);
})();
