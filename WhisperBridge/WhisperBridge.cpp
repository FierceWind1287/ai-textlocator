// WhisperBridge.cpp  ── 最小可编译&工作版本
#include <filesystem>                       // std::filesystem::path
#include <openvino/openvino.hpp>            // OpenVINO 基础
#include <openvino/genai/whisper_pipeline.hpp>
#include <string>
#include <memory>
#include <vector>

static std::unique_ptr<ov::genai::WhisperPipeline> g_pipe;

//-----------------------------------------------------------------------------
// 初始化：model_dir = 模型文件夹，device = "CPU"/"GPU"/"AUTO"
extern "C" __declspec(dllexport)
int __cdecl Init(const char* model_dir, const char* device) {
    try {
        std::filesystem::path modelsPath(model_dir);       // ① path 类型
        std::string dev = device ? device : "AUTO";        // ② string 类型

        g_pipe.reset(new ov::genai::WhisperPipeline(modelsPath, dev));
        return 0;                                          // 成功
    }
    catch (const std::exception&) {
        return -1;                                         // 失败
    }
}

//-----------------------------------------------------------------------------
// 语音 PCM（16 kHz mono float[-1,1]）→ UTF‑8 字符串
extern "C" __declspec(dllexport)
const char* __cdecl Transcribe(const float* pcm, int len) {
    static std::string result;

    if (!g_pipe) { result = "[Error] Pipeline not inited"; return result.c_str(); }

    std::vector<float> audio(pcm, pcm + len);
    auto decoded = g_pipe->generate(audio);        // Whisper 推理
    result = static_cast<std::string>(decoded);    // to string
    return result.c_str();
}
