#include <cmath>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>

#include "ozz/animation/runtime/animation.h"
#include "ozz/animation/runtime/local_to_model_job.h"
#include "ozz/animation/runtime/sampling_job.h"
#include "ozz/animation/runtime/skeleton.h"
#include "ozz/base/containers/vector.h"
#include "ozz/base/io/archive.h"
#include "ozz/base/io/stream.h"
#include "ozz/base/maths/simd_math.h"
#include "ozz/base/maths/soa_transform.h"

namespace {

template <typename T>
bool Load(const char* path, T* value) {
  ozz::io::File file(path, "rb");
  if (!file.opened()) {
    std::cerr << "Unable to open " << path << ".\n";
    return false;
  }
  ozz::io::IArchive archive(&file);
  if (!archive.TestTag<T>()) {
    std::cerr << "Unexpected ozz archive type in " << path << ".\n";
    return false;
  }
  archive >> *value;
  return true;
}

template <typename T>
void Write(std::ofstream& output, const T& value) {
  output.write(reinterpret_cast<const char*>(&value), sizeof(value));
}

void WriteString(std::ofstream& output, const char* value) {
  const auto length = static_cast<uint16_t>(std::char_traits<char>::length(value));
  Write(output, length);
  output.write(value, length);
}

}  // namespace

int main(int argc, const char** argv) {
  if (argc < 5) {
    std::cerr << "Usage: myth_rig_baker <skeleton.ozz> <output.mrig> <sample-rate> <animation.ozz>...\n";
    return 1;
  }

  ozz::animation::Skeleton skeleton;
  if (!Load(argv[1], &skeleton)) return 1;

  const float sample_rate = std::stof(argv[3]);
  if (!(sample_rate > 0)) {
    std::cerr << "Sample rate must be positive.\n";
    return 1;
  }

  std::ofstream output(argv[2], std::ios::binary);
  if (!output) {
    std::cerr << "Unable to create " << argv[2] << ".\n";
    return 1;
  }

  Write(output, uint32_t{0x4749524d});  // MRIG
  Write(output, uint32_t{1});
  Write(output, static_cast<uint16_t>(skeleton.num_joints()));
  for (int i = 0; i < skeleton.num_joints(); ++i) {
    WriteString(output, skeleton.joint_names()[i]);
    Write(output, skeleton.joint_parents()[i]);
  }

  Write(output, static_cast<uint16_t>(argc - 4));
  ozz::vector<ozz::math::SoaTransform> locals(skeleton.num_soa_joints());
  ozz::vector<ozz::math::Float4x4> models(skeleton.num_joints());
  ozz::animation::SamplingJob::Context context(skeleton.num_joints());

  for (int argument = 4; argument < argc; ++argument) {
    ozz::animation::Animation animation;
    if (!Load(argv[argument], &animation)) return 1;
    if (animation.num_tracks() != skeleton.num_joints()) {
      std::cerr << animation.name() << " has " << animation.num_tracks()
                << " tracks; expected " << skeleton.num_joints() << ".\n";
      return 1;
    }

    const auto sample_count = static_cast<uint32_t>(std::ceil(animation.duration() * sample_rate)) + 1;
    WriteString(output, animation.name());
    Write(output, animation.duration());
    Write(output, sample_count);

    for (uint32_t frame = 0; frame < sample_count; ++frame) {
      ozz::animation::SamplingJob sample;
      sample.animation = &animation;
      sample.context = &context;
      sample.ratio = frame / static_cast<float>(sample_count - 1);
      sample.output = make_span(locals);
      if (!sample.Run()) return 1;

      ozz::animation::LocalToModelJob local_to_model;
      local_to_model.skeleton = &skeleton;
      local_to_model.input = make_span(locals);
      local_to_model.output = make_span(models);
      if (!local_to_model.Run()) return 1;

      for (const auto& model : models) {
        Write(output, ozz::math::GetX(model.cols[3]));
        Write(output, ozz::math::GetY(model.cols[3]));
        Write(output, ozz::math::GetZ(model.cols[3]));
      }
    }
  }

  if (!output) {
    std::cerr << "Failed writing " << argv[2] << ".\n";
    return 1;
  }
  std::cout << "Wrote " << argv[2] << " with " << skeleton.num_joints()
            << " joints and " << argc - 4 << " clips.\n";
}

