// MIT. Build adapter, not a replacement implementation of Uno's GL shim.
// The build copies the installed Uno source beside this file without modification.
// Parse system headers normally, then preserve the shim's exported C ABI.
#include <GLES3/gl3.h>
#include <emscripten.h>
#include <emscripten/html5_webgl.h>
#include <string.h>
#include <stdint.h>

extern "C" {
#include "uno_gl_shim.h"
}
