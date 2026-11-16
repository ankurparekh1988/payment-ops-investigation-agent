// Runs in <head> before the stylesheets. The server can't see the OS colour preference, so this records it in a
// cookie for the next prerender, and marks <html> so the page background matches before Blazor starts.
(function () {
  function read(name) {
    var match = document.cookie.match('(?:^|; )' + name + '=([^;]*)');
    return match ? match[1] : null;
  }

  function write(name, value) {
    document.cookie = name + '=' + value + '; path=/; max-age=31536000; samesite=lax' +
      (location.protocol === 'https:' ? '; secure' : '');
  }

  function valid(value) {
    return value === 'dark' || value === 'light' ? value : null;
  }

  var chosen = valid(read('po-theme'));
  var system = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  if (!chosen) {
    write('po-theme-system', system);
  }

  document.documentElement.setAttribute('data-theme', chosen || system);

  window.poTheme = {
    // The person's explicit choice, from the toggle.
    set: function (mode) {
      write('po-theme', mode);
      document.documentElement.setAttribute('data-theme', mode);
    },
    // An OS change, which applies only while the person hasn't chosen.
    system: function (mode) {
      if (!valid(read('po-theme'))) {
        write('po-theme-system', mode);
        document.documentElement.setAttribute('data-theme', mode);
      }
    }
  };
})();
