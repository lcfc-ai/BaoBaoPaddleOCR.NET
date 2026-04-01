cmake_minimum_required(VERSION 3.20)
project(BaoBaoPaddleOCR.Native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if (MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

option(BAOBAO_WITH_PADDLEOCR "Enable official PaddleOCR C++ integration" OFF)
option(BAOBAO_WITH_GPU "Build with GPU inference support" OFF)
option(BAOBAO_WITH_MKL "Build with MKL/MKLDNN support" ON)
option(BAOBAO_WITH_STATIC_LIB "Link Paddle Inference static library" ON)

add_library(BaoBaoPaddleOCR.Native SHARED
    src/baobao_paddleocr.cpp
)

target_include_directories(BaoBaoPaddleOCR.Native
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/include/shims
)

if (BAOBAO_WITH_PADDLEOCR)
    target_compile_definitions(BaoBaoPaddleOCR.Native PRIVATE BAOBAO_WITH_PADDLEOCR=1)

    set(BAOBAO_PADDLEOCR_SRC_DIR "" CACHE PATH "PaddleOCR repo root path")
    set(BAOBAO_PADDLE_INFERENCE_DIR "" CACHE PATH "Paddle inference root path")
    set(BAOBAO_OPENCV_DIR "" CACHE PATH "OpenCV root path or OpenCV_DIR")
    set(BAOBAO_CUDA_LIB_DIR "" CACHE PATH "CUDA library directory when BAOBAO_WITH_GPU=ON")
    set(BAOBAO_CUDNN_LIB_DIR "" CACHE PATH "cuDNN library directory when BAOBAO_WITH_GPU=ON")

    if (BAOBAO_PADDLEOCR_SRC_DIR STREQUAL "")
        set(_baobao_default_paddleocr_src "${CMAKE_CURRENT_SOURCE_DIR}/../PaddleOCR-3.4.0")
        if (EXISTS "${_baobao_default_paddleocr_src}")
            set(BAOBAO_PADDLEOCR_SRC_DIR "${_baobao_default_paddleocr_src}" CACHE PATH "PaddleOCR repo root path" FORCE)
        endif()
    endif()
    if (BAOBAO_OPENCV_DIR STREQUAL "")
        if (EXISTS "C:/opencv/build")
            set(BAOBAO_OPENCV_DIR "C:/opencv/build" CACHE PATH "OpenCV root path or OpenCV_DIR" FORCE)
        elseif (EXISTS "C:/opencv")
            set(BAOBAO_OPENCV_DIR "C:/opencv" CACHE PATH "OpenCV root path or OpenCV_DIR" FORCE)
        endif()
    endif()

    if (BAOBAO_PADDLEOCR_SRC_DIR STREQUAL "")
        message(FATAL_ERROR "BAOBAO_PADDLEOCR_SRC_DIR is required when BAOBAO_WITH_PADDLEOCR=ON.")
    endif()
    if (BAOBAO_PADDLE_INFERENCE_DIR STREQUAL "")
        message(FATAL_ERROR "BAOBAO_PADDLE_INFERENCE_DIR is required when BAOBAO_WITH_PADDLEOCR=ON.")
    endif()
    if (BAOBAO_OPENCV_DIR STREQUAL "")
        message(FATAL_ERROR "BAOBAO_OPENCV_DIR is required when BAOBAO_WITH_PADDLEOCR=ON.")
    endif()

    message(STATUS "BAOBAO_WITH_PADDLEOCR=ON")
    message(STATUS "BAOBAO_PADDLEOCR_SRC_DIR=${BAOBAO_PADDLEOCR_SRC_DIR}")
    message(STATUS "BAOBAO_PADDLE_INFERENCE_DIR=${BAOBAO_PADDLE_INFERENCE_DIR}")
    message(STATUS "BAOBAO_OPENCV_DIR=${BAOBAO_OPENCV_DIR}")

    set(BAOBAO_CPP_INFER_DIR "${BAOBAO_PADDLEOCR_SRC_DIR}/deploy/cpp_infer")
    if (NOT EXISTS "${BAOBAO_CPP_INFER_DIR}/src")
        message(FATAL_ERROR "Cannot find PaddleOCR cpp_infer sources under ${BAOBAO_CPP_INFER_DIR}.")
    endif()

    if (EXISTS "${BAOBAO_OPENCV_DIR}/OpenCVConfig.cmake")
        set(OpenCV_DIR "${BAOBAO_OPENCV_DIR}")
    elseif (EXISTS "${BAOBAO_OPENCV_DIR}/x64/vc16/lib/OpenCVConfig.cmake")
        set(OpenCV_DIR "${BAOBAO_OPENCV_DIR}/x64/vc16/lib")
    else()
        message(FATAL_ERROR "Cannot resolve OpenCVConfig.cmake from BAOBAO_OPENCV_DIR=${BAOBAO_OPENCV_DIR}.")
    endif()

    find_package(OpenCV REQUIRED)

    set(PADDLE_LIB "${BAOBAO_PADDLE_INFERENCE_DIR}")
    file(GLOB_RECURSE BAOBAO_PADDLEOCR_CPP_SOURCES CONFIGURE_DEPENDS
        "${BAOBAO_CPP_INFER_DIR}/src/*.cc"
    )
    target_sources(BaoBaoPaddleOCR.Native PRIVATE ${BAOBAO_PADDLEOCR_CPP_SOURCES})

    function(baobao_download_and_decompress url filename decompress_dir)
        if (NOT EXISTS "${filename}" AND NOT EXISTS "${decompress_dir}")
            message(STATUS "Downloading ${url}")
            file(DOWNLOAD "${url}" "${filename}.tmp" SHOW_PROGRESS)
            file(RENAME "${filename}.tmp" "${filename}")
        endif()
        if (NOT EXISTS "${decompress_dir}")
            file(MAKE_DIRECTORY "${decompress_dir}")
            message(STATUS "Decompressing ${filename}")
            execute_process(COMMAND ${CMAKE_COMMAND} -E tar -xf "${filename}" WORKING_DIRECTORY "${decompress_dir}")
        endif()
    endfunction()

    set(BAOBAO_THIRD_PARTY_PATH "${BAOBAO_CPP_INFER_DIR}/third_party")
    file(MAKE_DIRECTORY "${BAOBAO_THIRD_PARTY_PATH}")
    foreach(BAOBAO_PKG abseil-cpp clipper_ver6.4.2 nlohmann)
        set(BAOBAO_PKG_URL "https://paddle-model-ecology.bj.bcebos.com/paddlex/cpp/libs/${BAOBAO_PKG}.tgz")
        set(BAOBAO_PKG_TGZ_PATH "${CMAKE_CURRENT_BINARY_DIR}/${BAOBAO_PKG}.tgz")
        set(BAOBAO_PKG_DST_PATH "${BAOBAO_THIRD_PARTY_PATH}/${BAOBAO_PKG}")
        baobao_download_and_decompress("${BAOBAO_PKG_URL}" "${BAOBAO_PKG_TGZ_PATH}" "${BAOBAO_PKG_DST_PATH}")
    endforeach()

    add_subdirectory("${BAOBAO_THIRD_PARTY_PATH}/abseil-cpp" "${CMAKE_CURRENT_BINARY_DIR}/abseil-cpp")
    add_subdirectory("${BAOBAO_THIRD_PARTY_PATH}/clipper_ver6.4.2/cpp" "${CMAKE_CURRENT_BINARY_DIR}/clipper")

    target_include_directories(BaoBaoPaddleOCR.Native PRIVATE
        ${OpenCV_INCLUDE_DIRS}
        "${BAOBAO_CPP_INFER_DIR}"
        "${PADDLE_LIB}/paddle/include"
        "${PADDLE_LIB}/third_party/install/protobuf/include"
        "${PADDLE_LIB}/third_party/install/glog/include"
        "${PADDLE_LIB}/third_party/install/gflags/include"
        "${PADDLE_LIB}/third_party/install/xxhash/include"
        "${PADDLE_LIB}/third_party/install/zlib/include"
        "${PADDLE_LIB}/third_party/install/onnxruntime/include"
        "${PADDLE_LIB}/third_party/install/paddle2onnx/include"
        "${PADDLE_LIB}/third_party/install/yaml-cpp/include"
        "${PADDLE_LIB}/third_party/install/openvino/include"
        "${PADDLE_LIB}/third_party/install/tbb/include"
        "${PADDLE_LIB}/third_party/boost"
        "${PADDLE_LIB}/third_party/eigen3"
        "${BAOBAO_THIRD_PARTY_PATH}/clipper_ver6.4.2/cpp"
    )

    if (BAOBAO_WITH_MKL)
        target_compile_definitions(BaoBaoPaddleOCR.Native PRIVATE USE_MKL)
        target_include_directories(BaoBaoPaddleOCR.Native PRIVATE
            "${PADDLE_LIB}/third_party/install/mklml/include"
        )
        if (EXISTS "${PADDLE_LIB}/third_party/install/onednn/include")
            target_include_directories(BaoBaoPaddleOCR.Native PRIVATE
                "${PADDLE_LIB}/third_party/install/onednn/include"
            )
        endif()
    endif()

    if (WIN32)
        target_compile_definitions(BaoBaoPaddleOCR.Native PRIVATE GOOGLE_GLOG_DLL_DECL=)
        if (BAOBAO_WITH_STATIC_LIB)
            target_compile_definitions(BaoBaoPaddleOCR.Native PRIVATE STATIC_LIB YAML_CPP_STATIC_DEFINE)
        endif()
    endif()

    set(BAOBAO_DEPS "")
    list(APPEND BAOBAO_DEPS ${OpenCV_LIBS} absl::statusor polyclipping)

    if (BAOBAO_WITH_STATIC_LIB)
        list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/paddle/lib/paddle_inference${CMAKE_STATIC_LIBRARY_SUFFIX}")
    else()
        list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/paddle/lib/paddle_inference${CMAKE_SHARED_LIBRARY_SUFFIX}")
    endif()

    if (BAOBAO_WITH_MKL)
        list(APPEND BAOBAO_DEPS
            "${PADDLE_LIB}/third_party/install/mklml/lib/mklml.lib"
            "${PADDLE_LIB}/third_party/install/mklml/lib/libiomp5md.lib"
        )
        if (EXISTS "${PADDLE_LIB}/third_party/install/onednn/lib/mkldnn.lib")
            list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/third_party/install/onednn/lib/mkldnn.lib")
        endif()
    else()
        list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/third_party/install/openblas/lib/openblas${CMAKE_STATIC_LIBRARY_SUFFIX}")
    endif()

    list(APPEND BAOBAO_DEPS
        "${PADDLE_LIB}/third_party/install/glog/lib/glog.lib"
        "${PADDLE_LIB}/third_party/install/gflags/lib/gflags_static.lib"
        "${PADDLE_LIB}/third_party/install/protobuf/lib/libprotobuf.lib"
        "${PADDLE_LIB}/third_party/install/xxhash/lib/xxhash.lib"
        libcmt
        shlwapi
    )

    if (EXISTS "${PADDLE_LIB}/third_party/install/yaml-cpp/lib/yaml-cpp.lib")
        list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/third_party/install/yaml-cpp/lib/yaml-cpp.lib")
    endif()
    if (EXISTS "${PADDLE_LIB}/third_party/install/snappy/lib/snappy.lib")
        list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/third_party/install/snappy/lib/snappy.lib")
    endif()
    if (EXISTS "${PADDLE_LIB}/third_party/install/snappystream/lib/snappystream.lib")
        list(APPEND BAOBAO_DEPS "${PADDLE_LIB}/third_party/install/snappystream/lib/snappystream.lib")
    endif()

    if (BAOBAO_WITH_GPU)
        if (BAOBAO_CUDA_LIB_DIR STREQUAL "")
            message(FATAL_ERROR "BAOBAO_CUDA_LIB_DIR is required when BAOBAO_WITH_GPU=ON.")
        endif()
        if (BAOBAO_CUDNN_LIB_DIR STREQUAL "")
            message(FATAL_ERROR "BAOBAO_CUDNN_LIB_DIR is required when BAOBAO_WITH_GPU=ON.")
        endif()
        target_compile_definitions(BaoBaoPaddleOCR.Native PRIVATE WITH_GPU)
        list(APPEND BAOBAO_DEPS
            "${BAOBAO_CUDA_LIB_DIR}/cudart${CMAKE_STATIC_LIBRARY_SUFFIX}"
            "${BAOBAO_CUDA_LIB_DIR}/cublas${CMAKE_STATIC_LIBRARY_SUFFIX}"
            "${BAOBAO_CUDNN_LIB_DIR}/cudnn${CMAKE_STATIC_LIBRARY_SUFFIX}"
        )
    endif()

    target_link_libraries(BaoBaoPaddleOCR.Native PRIVATE ${BAOBAO_DEPS})
endif()

if (MSVC)
    target_compile_options(BaoBaoPaddleOCR.Native PRIVATE /utf-8 /W4 /EHsc)
endif()
