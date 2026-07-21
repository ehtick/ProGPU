#include <stddef.h>

// Link-only counterpart for WgpuContext's iOS static-library resolver import.
// Browser rendering obtains WebGPU from JavaScript and never calls this function.
void *progpu_wgpu_get_proc_address(const char *symbol)
{
    (void)symbol;
    return NULL;
}
