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

BAOBAO_API int BaoBaoPaddleOcr_Create(const char* model_root,
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
