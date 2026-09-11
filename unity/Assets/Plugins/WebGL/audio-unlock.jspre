// Bang's-Edge: start Web Audio inside the user's gesture itself.
// Unity reads input on the next frame, outside the browser's event handler, and browsers may only
// let an AudioContext start from inside that handler. The web version calls AudioController.init()
// on every pointerdown (script.js handlePointerDown), so do the same here, directly in the event.
// A finger only counts as a user gesture when it lifts: the HTML spec's activation-triggering events
// are pointerdown for a mouse but pointerup / touchend for touch. So on phones init() also runs on
// release; it resumes a suspended AudioContext, and the sound starts from the first release on.
// audio.jspre is the web version's audio.js plus a volume control and defines window.AudioController.
(function () {
  function startAudio() {
    var audio = window.AudioController;
    if (audio && typeof audio.init === 'function') audio.init();
  }
  document.addEventListener('pointerdown', startAudio, true);
  document.addEventListener('pointerup', startAudio, true);
  document.addEventListener('touchend', startAudio, true);
})();
