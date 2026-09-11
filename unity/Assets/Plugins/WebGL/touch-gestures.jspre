// Bang's-Edge: keep the phone's own touch gestures off the game canvas.
// unityroom serves only the files in Build/, so the CSS in our page template never reaches players
// there; this runs inside the build instead. Without it a long press can select text, open the
// context menu or the magnifier, a drag can scroll or zoom the page, and a tap is followed by
// emulated mousedown/mouseup events that Unity would read as a second press.
(function () {
  function guard(canvas) {
    if (!canvas || canvas.__bangsTouchGuard) return;
    canvas.__bangsTouchGuard = true;
    var style = canvas.style;
    style.touchAction = 'none';
    style.userSelect = 'none';
    style.webkitUserSelect = 'none';
    style.webkitTouchCallout = 'none';
    style.webkitTapHighlightColor = 'transparent';
    function block(event) { event.preventDefault(); }
    canvas.addEventListener('contextmenu', block);
    canvas.addEventListener('touchstart', block, { passive: false });
    canvas.addEventListener('touchmove', block, { passive: false });
  }
  guard((typeof Module !== 'undefined' && Module.canvas) || document.querySelector('canvas'));
})();
