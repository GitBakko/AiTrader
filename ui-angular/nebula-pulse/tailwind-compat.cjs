const Module = require('module');
const path = require('path');

try {
  const tailwindPath = require.resolve('tailwindcss');
  const plugin = require('@tailwindcss/postcss');

  if (plugin && typeof plugin === 'function' && !plugin.default) {
    plugin.default = plugin;
  }

  const shim = new Module(tailwindPath);
  shim.filename = tailwindPath;
  shim.paths = Module._nodeModulePaths(path.dirname(tailwindPath));
  shim.exports = plugin;
  shim.loaded = true;

  require.cache[tailwindPath] = shim;
} catch (error) {
  // eslint-disable-next-line no-console
  console.warn('[tailwind-compat] Failed to install Tailwind PostCSS shim:', error.message);
}
