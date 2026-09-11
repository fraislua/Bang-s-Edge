mergeInto(LibraryManager.library, {
  BangsAudio_StartDrone: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.startDrone === 'function') {
      window.AudioController.startDrone();
    }
  },

  BangsAudio_StopDrone: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.stopDrone === 'function') {
      window.AudioController.stopDrone();
    }
  },

  BangsAudio_UpdateWarning: function (dangerRatio, dt) {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.updateWarning === 'function') {
      window.AudioController.updateWarning(dangerRatio, dt);
    }
  },

  BangsAudio_PlayResolve: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.playResolve === 'function') {
      window.AudioController.playResolve();
    }
  },

  BangsAudio_PlayBigBang: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.playBigBang === 'function') {
      window.AudioController.playBigBang();
    }
  },

  BangsAudio_ToggleMute: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.toggleMute === 'function') {
      return window.AudioController.toggleMute() ? 1 : 0;
    }
    return 0;
  },

  BangsAudio_IsMuted: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.isMuted === 'function') {
      return window.AudioController.isMuted() ? 1 : 0;
    }
    return 0;
  },

  BangsAudio_SetVolume: function (volume) {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.setVolume === 'function') {
      window.AudioController.setVolume(volume);
    }
  },

  BangsAudio_GetVolume: function () {
    if (typeof window !== 'undefined' && window.AudioController && typeof window.AudioController.getVolume === 'function') {
      return window.AudioController.getVolume();
    }
    return 1;
  }
});
