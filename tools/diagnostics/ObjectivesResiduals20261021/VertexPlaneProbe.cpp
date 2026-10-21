// Offline OpticStudio SDK probe. This plane has no optical power and logs the
// coordinates delivered to a user-defined surface. Never used by the product.
#include "usersurf.h"
extern "C" { int _fltused = 0; }
#ifndef PROBE_ROTATIONAL
#define PROBE_ROTATIONAL 1
#endif

// No Windows SDK or CRT dependency: only these documented kernel32 imports.
extern "C" __declspec(dllimport) void* __stdcall CreateFileA(const char*, unsigned long, unsigned long, void*, unsigned long, unsigned long, void*);
extern "C" __declspec(dllimport) int __stdcall WriteFile(void*, const void*, unsigned long, unsigned long*, void*);
extern "C" __declspec(dllimport) int __stdcall CloseHandle(void*);
extern "C" __declspec(dllimport) void __stdcall AcquireSRWLockExclusive(void*);
extern "C" __declspec(dllimport) void __stdcall ReleaseSRWLockExclusive(void*);
static void* captureLock = nullptr;
static const char* capturePath = "D:\\Projects\\opticalsystem\\artifacts\\validation\\objectives32-continuation-20261021\\probe-rays.bin";
struct Record { double id, wave, wavelength, x, y, z, l, m, n, n1, n2; };
static void Name(USER_DATA* ray, const char* text) { int i = 0; do { ray->string[i] = text[i]; } while (text[i++] != 0); }

extern "C" __declspec(dllexport) int __stdcall UserDefinedSurface3(USER_DATA* ray, FIXED_DATA3* data)
{
    switch (data->type)
    {
        case 0:
            ray->string[0] = 0;
            if (data->numb == 0) Name(ray, "OSD Probe");
            if (data->numb == 1 && PROBE_ROTATIONAL) Name(ray, "1");
            break;
        case 1:
            ray->string[0] = 0;
            if (data->numb == 1) Name(ray, "Capture ID");
            break;
        case 2: ray->string[0] = 0; break;
        case 3: ray->sag1 = ray->sag2 = 0; break;
        case 4:
        case 5:
            if (data->n1 != data->n2) return -data->surf;
            ray->ln = ray->mn = 0; ray->nn = -1; ray->path = 0;
            if (data->type == 5)
            {
                AcquireSRWLockExclusive(&captureLock);
                void* stream = CreateFileA(capturePath, 4, 1, nullptr, 4, 128, nullptr);
                if (stream != (void*)-1)
                {
                    Record r;
                    r.id = data->param[1]; r.wave = data->wave; r.wavelength = data->wavelength;
                    r.x = ray->x; r.y = ray->y; r.z = ray->z;
                    r.l = ray->l; r.m = ray->m; r.n = ray->n; r.n1 = data->n1; r.n2 = data->n2;
                    unsigned long written;
                    WriteFile(stream, &r, sizeof(r), &written, nullptr);
                    CloseHandle(stream);
                }
                ReleaseSRWLockExclusive(&captureLock);
            }
            break;
        case 6:
            ray->index = data->n2; ray->dndx = ray->dndy = ray->dndz = 0; break;
    }
    return 0;
}
