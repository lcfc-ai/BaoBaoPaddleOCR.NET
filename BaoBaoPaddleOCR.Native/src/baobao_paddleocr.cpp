#include "baobao_paddleocr.h"

#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iomanip>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#if BAOBAO_WITH_PADDLEOCR
#include "src/pipelines/ocr/pipeline.h"
#endif

namespace {

struct OcrEngine {
  explicit OcrEngine(std::string model_root_value) : model_root(std::move(model_root_value)) {}
  std::string model_root;
#if BAOBAO_WITH_PADDLEOCR
  std::unique_ptr<_OCRPipeline> pipeline;
#endif
};

struct RuntimeOptions {
  bool enable_gpu = false;
  int gpu_device_id = 0;
  bool enable_mkldnn = true;
  int cpu_threads = 8;
};

std::string JsonEscape(const std::string& input) {
  std::string out;
  out.reserve(input.size() + 8);
  for (const unsigned char ch : input) {
    switch (ch) {
      case '\\':
        out += "\\\\";
        break;
      case '"':
        out += "\\\"";
        break;
      case '\n':
        out += "\\n";
        break;
      case '\r':
        out += "\\r";
        break;
      case '\t':
        out += "\\t";
        break;
      default:
        out.push_back(static_cast<char>(ch));
        break;
    }
  }
  return out;
}

char* AllocUtf8(const std::string& text) {
  auto* buf = static_cast<char*>(std::malloc(text.size() + 1));
  if (buf == nullptr) {
    return nullptr;
  }
  std::memcpy(buf, text.data(), text.size());
  buf[text.size()] = '\0';
  return buf;
}

void SetMessage(char** target, const std::string& msg) {
  if (target == nullptr) {
    return;
  }
  *target = AllocUtf8(msg);
}

bool IsNullOrBlank(const char* value) {
  if (value == nullptr) {
    return true;
  }
  for (const char* p = value; *p != '\0'; ++p) {
    if (!std::isspace(static_cast<unsigned char>(*p))) {
      return false;
    }
  }
  return true;
}

std::string Trim(std::string value) {
  const auto is_space = [](unsigned char ch) { return std::isspace(ch) != 0; };
  value.erase(value.begin(),
              std::find_if(value.begin(), value.end(),
                           [&](unsigned char ch) { return !is_space(ch); }));
  value.erase(std::find_if(value.rbegin(), value.rend(),
                           [&](unsigned char ch) { return !is_space(ch); })
                  .base(),
              value.end());
  return value;
}

std::string JoinLines(const std::vector<std::string>& lines) {
  std::ostringstream builder;
  bool first = true;
  for (const auto& line : lines) {
    if (line.empty()) {
      continue;
    }
    if (!first) {
      builder << '\n';
    }
    builder << line;
    first = false;
  }
  return builder.str();
}

std::string BuildMockJson(const std::string& text, const std::filesystem::path& image_path) {
  const auto escaped_text = JsonEscape(text);
  const auto escaped_name = JsonEscape(image_path.filename().string());
  std::string json;
  json.reserve(256 + escaped_text.size() + escaped_name.size());
  json += "{";
  json += "\"text\":\"";
  json += escaped_text;
  json += "\",\"blocks\":[{\"text\":\"";
  json += escaped_text;
  json += "\",\"score\":0.99,\"source\":\"";
  json += escaped_name;
  json += "\"}]}";
  return json;
}

#if BAOBAO_WITH_PADDLEOCR
std::filesystem::path GetEnvPath(const char* env_name) {
  const char* value = std::getenv(env_name);
  if (value == nullptr || *value == '\0') {
    return {};
  }
  return std::filesystem::path(value);
}

bool GetEnvBool(const char* env_name, bool default_value) {
  const char* raw = std::getenv(env_name);
  if (raw == nullptr) {
    return default_value;
  }

  auto value = Trim(raw);
  std::transform(value.begin(), value.end(), value.begin(),
                 [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
  if (value == "1" || value == "true" || value == "yes" || value == "on") {
    return true;
  }
  if (value == "0" || value == "false" || value == "no" || value == "off") {
    return false;
  }
  return default_value;
}

int GetEnvInt(const char* env_name, int default_value) {
  const char* raw = std::getenv(env_name);
  if (raw == nullptr || *raw == '\0') {
    return default_value;
  }

  try {
    return std::stoi(raw);
  } catch (...) {
    return default_value;
  }
}

std::filesystem::path ResolveChildDirectory(
    const std::filesystem::path& model_root,
    const char* override_env_name,
    std::initializer_list<std::string_view> aliases,
    bool required) {
  const auto from_env = GetEnvPath(override_env_name);
  if (!from_env.empty()) {
    const auto candidate = from_env.is_absolute() ? from_env : (model_root / from_env);
    if (std::filesystem::is_directory(candidate)) {
      return candidate;
    }
    throw std::runtime_error(std::string("Model directory from ") + override_env_name +
                             " does not exist: " + candidate.string());
  }

  for (const auto alias : aliases) {
    const auto candidate = model_root / std::string(alias);
    if (std::filesystem::is_directory(candidate)) {
      return candidate;
    }
  }

  if (!required) {
    return {};
  }

  std::ostringstream message;
  message << "Cannot find required model subdirectory under " << model_root.string()
          << ". Checked aliases: ";
  bool first = true;
  for (const auto alias : aliases) {
    if (!first) {
      message << ", ";
    }
    message << alias;
    first = false;
  }
  message << ". You can override with " << override_env_name << ".";
  throw std::runtime_error(message.str());
}

std::string BuildRealJson(const OCRPipelineResult& result) {
  std::vector<std::string> non_empty_lines;
  non_empty_lines.reserve(result.rec_texts.size());
  for (const auto& text : result.rec_texts) {
    const auto trimmed = Trim(text);
    if (!trimmed.empty()) {
      non_empty_lines.push_back(trimmed);
    }
  }

  std::ostringstream json;
  json << "{";
  json << "\"text\":\"" << JsonEscape(JoinLines(non_empty_lines)) << "\",";
  json << "\"blocks\":[";
  const auto block_count = std::min(result.rec_texts.size(), result.rec_scores.size());
  for (size_t i = 0; i < block_count; ++i) {
    if (i > 0) {
      json << ',';
    }
    json << "{";
    json << "\"text\":\"" << JsonEscape(result.rec_texts[i]) << "\",";
    json << "\"score\":" << std::fixed << std::setprecision(6) << result.rec_scores[i];
    if (i < result.rec_polys.size()) {
      json << ",\"box\":[";
      for (size_t p = 0; p < result.rec_polys[i].size(); ++p) {
        if (p > 0) {
          json << ',';
        }
        json << '[' << std::fixed << std::setprecision(3) << result.rec_polys[i][p].x << ','
             << std::fixed << std::setprecision(3) << result.rec_polys[i][p].y << ']';
      }
      json << "]";
    }
    json << "}";
  }
  json << "]}";
  return json.str();
}

std::string InferModelNameFromDirectory(const std::filesystem::path& model_dir,
                                        std::string_view model_kind) {
  const auto dir_name = model_dir.filename().string();

  if (dir_name.find("PP-OCRv5_mobile_") != std::string::npos) {
    return std::string("PP-OCRv5_mobile_") + std::string(model_kind);
  }

  if (dir_name.find("PP-OCRv5_server_") != std::string::npos) {
    return std::string("PP-OCRv5_server_") + std::string(model_kind);
  }

  if (dir_name.find("PP-OCRv4_mobile_") != std::string::npos) {
    return std::string("PP-OCRv4_mobile_") + std::string(model_kind);
  }

  if (dir_name.find("PP-OCRv4_server_") != std::string::npos) {
    return std::string("PP-OCRv4_server_") + std::string(model_kind);
  }

  return {};
}

RuntimeOptions GetRuntimeOptions(const BaoBaoPaddleOcrCreateOptions* options) {
  RuntimeOptions runtime_options;
  runtime_options.enable_mkldnn = GetEnvBool("BAOBAO_PADDLEOCR_ENABLE_MKLDNN", true);
  runtime_options.cpu_threads = GetEnvInt("BAOBAO_PADDLEOCR_CPU_THREADS", 8);

  const auto device_from_env = Trim(std::getenv("BAOBAO_PADDLEOCR_DEVICE") == nullptr
                                        ? ""
                                        : std::getenv("BAOBAO_PADDLEOCR_DEVICE"));
  if (!device_from_env.empty()) {
    auto value = device_from_env;
    std::transform(value.begin(), value.end(), value.begin(),
                   [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    if (value == "gpu" || value.rfind("gpu:", 0) == 0) {
      runtime_options.enable_gpu = true;
      const auto colon_pos = value.find(':');
      if (colon_pos != std::string::npos) {
        try {
          runtime_options.gpu_device_id = std::stoi(value.substr(colon_pos + 1));
        } catch (...) {
          runtime_options.gpu_device_id = 0;
        }
      }
    }
  }

  if (options == nullptr) {
    return runtime_options;
  }

  if (options->enable_gpu >= 0) {
    runtime_options.enable_gpu = options->enable_gpu != 0;
  }

  if (options->gpu_device_id >= 0) {
    runtime_options.gpu_device_id = options->gpu_device_id;
  }

  if (options->enable_mkldnn >= 0) {
    runtime_options.enable_mkldnn = options->enable_mkldnn != 0;
  }

  if (options->cpu_threads > 0) {
    runtime_options.cpu_threads = options->cpu_threads;
  }

  return runtime_options;
}

std::unique_ptr<_OCRPipeline> CreatePipeline(const std::filesystem::path& model_root,
                                             const RuntimeOptions& runtime_options) {
  const auto det_dir =
      ResolveChildDirectory(model_root, "BAOBAO_PADDLEOCR_DET_DIRNAME",
                            {"PP-OCRv5_mobile_det_infer", "PP-OCRv5_server_det_infer",
                             "det", "text_detection", "TextDetection"},
                            true);
  const auto rec_dir =
      ResolveChildDirectory(model_root, "BAOBAO_PADDLEOCR_REC_DIRNAME",
                            {"PP-OCRv5_mobile_rec_infer", "PP-OCRv5_server_rec_infer",
                             "rec", "text_recognition", "TextRecognition"},
                            true);
  const auto cls_dir =
      ResolveChildDirectory(model_root, "BAOBAO_PADDLEOCR_CLS_DIRNAME",
                            {"PP-LCNet_x1_0_textline_ori_infer", "cls",
                             "textline_orientation", "TextLineOrientation"},
                            false);

  OCRPipelineParams params;
  params.text_detection_model_dir = det_dir.string();
  params.text_detection_model_name = InferModelNameFromDirectory(det_dir, "det");
  params.text_recognition_model_dir = rec_dir.string();
  params.text_recognition_model_name = InferModelNameFromDirectory(rec_dir, "rec");
  params.use_doc_orientation_classify = false;
  params.use_doc_unwarping = false;
  params.use_textline_orientation = !cls_dir.empty();
  if (!cls_dir.empty()) {
    params.textline_orientation_model_dir = cls_dir.string();
  }
  params.device = runtime_options.enable_gpu
                      ? "gpu:" + std::to_string(runtime_options.gpu_device_id)
                      : "cpu";
  params.enable_mkldnn = runtime_options.enable_mkldnn;
  params.cpu_threads = runtime_options.cpu_threads;
  params.precision = "fp32";
  params.thread_num = 1;

  return std::make_unique<_OCRPipeline>(params);
}
#endif

}  // namespace

int BaoBaoPaddleOcr_Create(const char* model_root,
                           BaoBaoPaddleOcrHandle* out_handle,
                           char** error_message) {
  return BaoBaoPaddleOcr_CreateWithOptions(model_root, nullptr, out_handle,
                                           error_message);
}

int BaoBaoPaddleOcr_CreateWithOptions(const char* model_root,
                                      const BaoBaoPaddleOcrCreateOptions* options,
                                      BaoBaoPaddleOcrHandle* out_handle,
                                      char** error_message) {
  if (out_handle == nullptr) {
    SetMessage(error_message, "out_handle is null.");
    return 1001;
  }
  *out_handle = nullptr;

  if (IsNullOrBlank(model_root)) {
    SetMessage(error_message, "model_root is empty.");
    return 1002;
  }

  const auto root = std::filesystem::path(model_root);
  if (!std::filesystem::exists(root)) {
    SetMessage(error_message, "model_root does not exist: " + root.string());
    return 1003;
  }

  try {
    auto engine = std::make_unique<OcrEngine>(root.string());
#if BAOBAO_WITH_PADDLEOCR
    engine->pipeline = CreatePipeline(root, GetRuntimeOptions(options));
#endif
    *out_handle = reinterpret_cast<BaoBaoPaddleOcrHandle>(engine.release());
    return 0;
  } catch (const std::exception& ex) {
    SetMessage(error_message, ex.what());
    return 1004;
  } catch (...) {
    SetMessage(error_message, "Unknown error while creating OCR engine.");
    return 1005;
  }
}

int BaoBaoPaddleOcr_Detect(BaoBaoPaddleOcrHandle handle,
                           const char* image_path,
                           char** json_result,
                           char** error_message) {
  if (json_result != nullptr) {
    *json_result = nullptr;
  }

  if (handle == nullptr) {
    SetMessage(error_message, "handle is null.");
    return 2001;
  }

  if (IsNullOrBlank(image_path)) {
    SetMessage(error_message, "image_path is empty.");
    return 2002;
  }

  const auto image = std::filesystem::path(image_path);
  if (!std::filesystem::exists(image)) {
    SetMessage(error_message, "image_path does not exist: " + image.string());
    return 2003;
  }

#if BAOBAO_WITH_PADDLEOCR
  try {
    auto* engine = reinterpret_cast<OcrEngine*>(handle);
    if (engine->pipeline == nullptr) {
      SetMessage(error_message, "PaddleOCR pipeline is not initialized.");
      return 2996;
    }

    engine->pipeline->Predict({image.string()});
    const auto results = engine->pipeline->PipelineResult();
    if (results.empty()) {
      SetMessage(error_message, "PaddleOCR returned no results.");
      return 2995;
    }

    const auto json = BuildRealJson(results.front());
    auto* buffer = AllocUtf8(json);
    if (buffer == nullptr) {
      SetMessage(error_message, "Out of memory while building OCR JSON.");
      return 2999;
    }
    *json_result = buffer;
    return 0;
  } catch (const std::exception& ex) {
    SetMessage(error_message, ex.what());
    return 2994;
  } catch (...) {
    SetMessage(error_message, "Unknown PaddleOCR inference error.");
    return 2993;
  }
#else
  const char* mock = std::getenv("BAOBAO_PADDLEOCR_MOCK_TEXT");
  if (mock != nullptr && std::strlen(mock) > 0) {
    const auto json = BuildMockJson(mock, image);
    auto* buffer = AllocUtf8(json);
    if (buffer == nullptr) {
      SetMessage(error_message, "Out of memory while building OCR JSON.");
      return 2999;
    }
    *json_result = buffer;
    return 0;
  }

  SetMessage(
      error_message,
      "Native DLL is built without PaddleOCR runtime integration. "
      "Set BAOBAO_PADDLEOCR_MOCK_TEXT for mock output, or rebuild with BAOBAO_WITH_PADDLEOCR=ON.");
  return 2997;
#endif
}

void BaoBaoPaddleOcr_Destroy(BaoBaoPaddleOcrHandle handle) {
  auto* engine = reinterpret_cast<OcrEngine*>(handle);
  delete engine;
}

void BaoBaoPaddleOcr_Free(void* ptr) {
  std::free(ptr);
}
