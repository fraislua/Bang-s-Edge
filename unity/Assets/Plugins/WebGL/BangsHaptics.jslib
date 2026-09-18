mergeInto(LibraryManager.library, {
  BangsHaptics_Vibrate: function (milliseconds) {
    try {
      if (typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function') {
        navigator.vibrate(milliseconds | 0);
      }
    } catch (e) {}
  }
});
