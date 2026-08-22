#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <memory>
#include <new>
#include <string>
#include <vector>

#include "ozz/animation/runtime/animation.h"
#include "ozz/animation/runtime/blending_job.h"
#include "ozz/animation/runtime/local_to_model_job.h"
#include "ozz/animation/runtime/sampling_job.h"
#include "ozz/animation/runtime/skeleton.h"
#include "ozz/base/containers/vector.h"
#include "ozz/base/io/archive.h"
#include "ozz/base/io/stream.h"
#include "ozz/base/maths/simd_math.h"
#include "ozz/base/maths/soa_transform.h"

#if defined(_WIN32)
#define MYTH_OZZ_EXPORT extern "C" __declspec(dllexport)
#else
#define MYTH_OZZ_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {

constexpr int kMaxLayers = 4;

class ReadStream final : public ozz::io::Stream {
 public:
  ReadStream(const uint8_t* data, size_t size) : data_(data), size_(size) {}
  bool opened() const override { return data_ != nullptr; }
  size_t Read(void* output, size_t size) override {
    const auto count = std::min(size, size_ - position_);
    std::memcpy(output, data_ + position_, count);
    position_ += count;
    return count;
  }
  size_t Write(const void*, size_t) override { return 0; }
  int Seek(int offset, Origin origin) override {
    const auto base = origin == kSet ? int64_t{0} : origin == kCurrent
        ? static_cast<int64_t>(position_) : static_cast<int64_t>(size_);
    const auto position = base + offset;
    if (position < 0 || static_cast<uint64_t>(position) > size_) return -1;
    position_ = static_cast<size_t>(position);
    return 0;
  }
  int Tell() const override { return static_cast<int>(position_); }
  size_t Size() const override { return size_; }

 private:
  const uint8_t* data_;
  size_t size_;
  size_t position_ = 0;
};

struct Reader {
  const uint8_t* at;
  const uint8_t* end;

  bool U32(uint32_t* value) {
    if (end - at < 4) return false;
    *value = static_cast<uint32_t>(at[0]) |
        static_cast<uint32_t>(at[1]) << 8 |
        static_cast<uint32_t>(at[2]) << 16 |
        static_cast<uint32_t>(at[3]) << 24;
    at += 4;
    return true;
  }

  bool Blob(const uint8_t** data, uint32_t* size) {
    if (!U32(size) || static_cast<uint64_t>(end - at) < *size) return false;
    *data = at;
    at += *size;
    return true;
  }
};

template <typename T>
bool Load(const uint8_t* data, size_t size, T* value) {
  ReadStream stream(data, size);
  ozz::io::IArchive archive(&stream);
  if (!archive.TestTag<T>()) return false;
  archive >> *value;
  return stream.Tell() == static_cast<int>(size);
}

void Error(char* output, int capacity, const char* message) {
  if (!output || capacity <= 0) return;
  std::strncpy(output, message, static_cast<size_t>(capacity - 1));
  output[capacity - 1] = '\0';
}

struct Rig {
  ozz::animation::Skeleton skeleton;
  std::vector<ozz::animation::Animation> clips;
};

struct Instance {
  explicit Instance(Rig* rig) : rig(rig), blended(rig->skeleton.num_soa_joints()),
      models(rig->skeleton.num_joints()) {
    for (int i = 0; i < kMaxLayers; ++i) {
      contexts[i] = std::make_unique<ozz::animation::SamplingJob::Context>(rig->skeleton.num_joints());
      locals[i].resize(rig->skeleton.num_soa_joints());
    }
  }

  Rig* rig;
  std::array<std::unique_ptr<ozz::animation::SamplingJob::Context>, kMaxLayers> contexts;
  std::array<ozz::vector<ozz::math::SoaTransform>, kMaxLayers> locals;
  ozz::vector<ozz::math::SoaTransform> blended;
  ozz::vector<ozz::math::Float4x4> models;
};

}  // namespace

MYTH_OZZ_EXPORT Rig* myth_ozz_rig_load(const uint8_t* data, int size, char* error, int error_capacity) {
  if (!data || size < 16) {
    Error(error, error_capacity, "Rig pack is empty or truncated.");
    return nullptr;
  }
  try {
  Reader reader{data, data + size};
  uint32_t magic, version;
  if (!reader.U32(&magic) || magic != 0x50525a4f || !reader.U32(&version) || version != 1) {
    Error(error, error_capacity, "Unsupported ozz rig pack.");
    return nullptr;
  }

  auto rig = std::make_unique<Rig>();
  const uint8_t* blob;
  uint32_t blob_size;
  if (!reader.Blob(&blob, &blob_size) || !Load(blob, blob_size, &rig->skeleton)) {
    Error(error, error_capacity, "Unable to load ozz skeleton.");
    return nullptr;
  }
  uint32_t clip_count;
  if (!reader.U32(&clip_count) || clip_count == 0 || clip_count > 65535) {
    Error(error, error_capacity, "Rig pack has an invalid clip count.");
    return nullptr;
  }
  rig->clips.resize(clip_count);
  for (uint32_t i = 0; i < clip_count; ++i)
    if (!reader.Blob(&blob, &blob_size) || !Load(blob, blob_size, &rig->clips[i]) ||
        rig->clips[i].num_tracks() != rig->skeleton.num_joints()) {
      Error(error, error_capacity, "Unable to load ozz animation.");
      return nullptr;
    }
  if (reader.at != reader.end) {
    Error(error, error_capacity, "Rig pack has trailing data.");
    return nullptr;
  }
  return rig.release();
  } catch (...) {
    Error(error, error_capacity, "Unable to allocate ozz rig.");
    return nullptr;
  }
}

MYTH_OZZ_EXPORT void myth_ozz_rig_destroy(Rig* rig) { delete rig; }
MYTH_OZZ_EXPORT int myth_ozz_rig_joint_count(const Rig* rig) {
  return rig ? rig->skeleton.num_joints() : 0;
}
MYTH_OZZ_EXPORT int myth_ozz_rig_clip_count(const Rig* rig) {
  return rig ? static_cast<int>(rig->clips.size()) : 0;
}
MYTH_OZZ_EXPORT const char* myth_ozz_rig_joint_name(const Rig* rig, int index) {
  return rig && index >= 0 && index < rig->skeleton.num_joints() ? rig->skeleton.joint_names()[index] : nullptr;
}
MYTH_OZZ_EXPORT const char* myth_ozz_rig_clip_name(const Rig* rig, int index) {
  return rig && index >= 0 && index < static_cast<int>(rig->clips.size()) ? rig->clips[index].name() : nullptr;
}
MYTH_OZZ_EXPORT float myth_ozz_rig_clip_duration(const Rig* rig, int index) {
  return rig && index >= 0 && index < static_cast<int>(rig->clips.size()) ? rig->clips[index].duration() : 0.f;
}
MYTH_OZZ_EXPORT Instance* myth_ozz_instance_create(Rig* rig) {
  if (!rig) return nullptr;
  try { return new Instance(rig); } catch (...) { return nullptr; }
}
MYTH_OZZ_EXPORT void myth_ozz_instance_destroy(Instance* instance) { delete instance; }

MYTH_OZZ_EXPORT int myth_ozz_instance_sample(Instance* instance, int layer_count,
    const int* clip_indices, const float* times, const float* weights, float* positions) {
  if (!instance || layer_count <= 0 || layer_count > kMaxLayers || !clip_indices ||
      !times || !weights || !positions) return 0;

  std::array<ozz::animation::BlendingJob::Layer, kMaxLayers> layers;
  for (int i = 0; i < layer_count; ++i) {
    const auto clip_index = clip_indices[i];
    if (clip_index < 0 || clip_index >= static_cast<int>(instance->rig->clips.size())) return 0;
    const auto& clip = instance->rig->clips[clip_index];
    ozz::animation::SamplingJob sample;
    sample.animation = &clip;
    sample.context = instance->contexts[i].get();
    sample.ratio = clip.duration() > 0 ? times[i] / clip.duration() : 0;
    sample.output = ozz::make_span(instance->locals[i]);
    if (!sample.Run()) return 0;
    layers[i].weight = weights[i];
    layers[i].transform = ozz::make_span(instance->locals[i]);
  }

  ozz::animation::BlendingJob blend;
  blend.layers = {layers.data(), static_cast<size_t>(layer_count)};
  blend.rest_pose = instance->rig->skeleton.joint_rest_poses();
  blend.output = ozz::make_span(instance->blended);
  if (!blend.Run()) return 0;

  ozz::animation::LocalToModelJob local_to_model;
  local_to_model.skeleton = &instance->rig->skeleton;
  local_to_model.input = ozz::make_span(instance->blended);
  local_to_model.output = ozz::make_span(instance->models);
  if (!local_to_model.Run()) return 0;

  for (int i = 0; i < instance->rig->skeleton.num_joints(); ++i) {
    positions[i * 3] = ozz::math::GetX(instance->models[i].cols[3]);
    positions[i * 3 + 1] = ozz::math::GetY(instance->models[i].cols[3]);
    positions[i * 3 + 2] = ozz::math::GetZ(instance->models[i].cols[3]);
  }
  return 1;
}
