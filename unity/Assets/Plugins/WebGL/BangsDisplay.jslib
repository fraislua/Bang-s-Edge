mergeInto(LibraryManager.library, {
  // Screen pixels (what Unity's Screen and Pointer report) per CSS pixel of the canvas.
  // Read from the canvas itself rather than window.devicePixelRatio: the page hosting the build
  // (unityroom serves its own page) may run Unity at a different pixel ratio than the device's.
  BangsDisplay_GetScreenPixelsPerCssPixel: function () {
    var canvas = Module.canvas;
    if (!canvas) return 1;
    var cssWidth = canvas.getBoundingClientRect().width;
    return cssWidth > 0 ? canvas.width / cssWidth : 1;
  },

  // 1 on devices whose main input is a finger, used before the first press to pick the HUD wording.
  BangsDisplay_IsCoarsePointer: function () {
    return (typeof window !== 'undefined' && window.matchMedia && window.matchMedia('(pointer: coarse)').matches) ? 1 : 0;
  }
});
