// WhisperBridge.cpp  ── Minimal compilable & working version
#include <filesystem>                       // std::filesystem::path
#include <openvino/openvino.hpp>            // OpenVINO base
#include <openvino/genai/whisper_pipeline.hpp>
#include <string>
#include <memory>
#include <vector>

static std::unique_ptr<ov::genai::WhisperPipeline> g_pipe;

//-----------------------------------------------------------------------------
// Initialization: model_dir = model folder, device = "CPU"/"GPU"/"AUTO"
extern "C" __declspec(dllexport)
int __cdecl Init(const char* model_dir, const char* device) {
    try {
        std::filesystem::path modelsPath(model_dir);       // ① path type
        std::string dev = device ? device : "AUTO";        // ② device string

        g_pipe.reset(new ov::genai::WhisperPipeline(modelsPath, dev));
        return 0;                                          // Success
    }
    catch (const std::exception&) {
        return -1;                                         // Failure
    }
}

//-----------------------------------------------------------------------------
// Speech PCM (16 kHz mono float[-1,1]) → UTF-8 string
extern "C" __declspec(dllexport)
const char* __cdecl Transcribe(const float* pcm, int len) {
    static std::string result;

    if (!g_pipe) {
        result = "[Error] Pipeline not initialized";
        return result.c_str();
    }

    std::vector<float> audio(pcm, pcm + len);
    auto decoded = g_pipe->generate(audio);        // Whisper inference
    result = static_cast<std::string>(decoded);    // Convert to string
    return result.c_str();
}
