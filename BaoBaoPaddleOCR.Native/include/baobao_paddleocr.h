#pragma once

#ifdef _WIN32
#define BAOBAO_API __declspec(dllexport)
#else
#define BAOBAO_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void* BaoBaoPaddleOcrHandle;

typedef struct BaoBaoPaddleOcrCreateOptions {
  int struct_size;
  int enable_gpu;
  int gpu_device_id;
  int enable_mkldnn;
  int cpu_threads;
} BaoBaoPaddleOcrCreateOptions;

BAOBAO_API int BaoBaoPaddleOcr_Create(const char* model_root,
                                      BaoBaoPaddleOcrHandle* out_handle,
                                      char** error_message);

BAOBAO_API int BaoBaoPaddleOcr_CreateWithOptions(
    const char* model_root,
    const BaoBaoPaddleOcrCreateOptions* options,
    BaoBaoPaddleOcrHandle* out_handle,
    char** error_message);

BAOBAO_API int BaoBaoPaddleOcr_Detect(BaoBaoPaddleOcrHandle handle,
                                      const char* image_path,
                                      char** json_result,
                                      char** error_message);

BAOBAO_API void BaoBaoPaddleOcr_Destroy(BaoBaoPaddleOcrHandle handle);

BAOBAO_API void BaoBaoPaddleOcr_Free(void* ptr);

#ifdef __cplusplus
}
#endif
