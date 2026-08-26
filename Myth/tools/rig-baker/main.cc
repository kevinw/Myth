#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

#include "ozz/animation/runtime/animation.h"
#include "ozz/animation/runtime/skeleton.h"
#include "ozz/base/io/archive.h"
#include "ozz/base/io/stream.h"

namespace {

void WriteU32(std::ofstream& output, uint32_t value) {
  const char bytes[] = {
      static_cast<char>(value), static_cast<char>(value >> 8),
      static_cast<char>(value >> 16), static_cast<char>(value >> 24)};
  output.write(bytes, sizeof(bytes));
}

bool Append(std::ofstream& output, const char* path) {
  std::ifstream input(path, std::ios::binary | std::ios::ate);
  if (!input) {
    std::cerr << "Unable to open " << path << ".\n";
    return false;
  }
  const auto end = input.tellg();
  if (end < 0 || static_cast<uint64_t>(end) > UINT32_MAX) {
    std::cerr << "File is too large: " << path << ".\n";
    return false;
  }
  const auto size = static_cast<uint32_t>(end);
  input.seekg(0);
  WriteU32(output, size);
  std::vector<char> bytes(size);
  input.read(bytes.data(), size);
  output.write(bytes.data(), size);
  if (!input || !output) {
    std::cerr << "Failed copying " << path << ".\n";
    return false;
  }
  return true;
}

int SkeletonJointCount(const char* path) {
  ozz::io::File file(path, "rb");
  if (!file.opened()) return -1;
  ozz::io::IArchive archive(&file);
  if (!archive.TestTag<ozz::animation::Skeleton>()) return -1;
  ozz::animation::Skeleton skeleton;
  archive >> skeleton;
  return skeleton.num_joints();
}

int AnimationTrackCount(const char* path) {
  ozz::io::File file(path, "rb");
  if (!file.opened()) return -1;
  ozz::io::IArchive archive(&file);
  if (!archive.TestTag<ozz::animation::Animation>()) return -1;
  ozz::animation::Animation animation;
  archive >> animation;
  return animation.num_tracks();
}

bool AppendAnimation(std::ofstream& output, const char* path, int joint_count) {
  const auto track_count = AnimationTrackCount(path);
  if (track_count < 0) {
    std::cerr << "Unable to load animation " << path << ".\n";
    return false;
  }
  if (track_count != joint_count) {
    std::cerr << path << " has " << track_count << " tracks; expected "
              << joint_count << ".\n";
    return false;
  }
  std::string name(path);
  const auto slash = name.find_last_of("/\\");
  if (slash != std::string::npos)
    name.erase(0, slash + 1);
  const auto extension = name.rfind('.');
  if (extension != std::string::npos)
    name.erase(extension);
  WriteU32(output, static_cast<uint32_t>(name.size()));
  output.write(name.data(), static_cast<std::streamsize>(name.size()));
  return Append(output, path);
}

}  // namespace

int main(int argc, const char** argv) {
  if (argc < 4) {
    std::cerr << "Usage: myth_rig_pack <output.ozzpack> <skeleton.ozz> <animation.ozz>...\n";
    return 1;
  }

  const auto joint_count = SkeletonJointCount(argv[2]);
  if (joint_count < 0) {
    std::cerr << "Unable to load skeleton " << argv[2] << ".\n";
    return 1;
  }

  std::ofstream output(argv[1], std::ios::binary);
  if (!output) {
    std::cerr << "Unable to create " << argv[1] << ".\n";
    return 1;
  }
  WriteU32(output, 0x50525a4f);  // OZRP
  WriteU32(output, 2);
  if (!Append(output, argv[2])) return 1;
  WriteU32(output, static_cast<uint32_t>(argc - 3));
  for (int argument = 3; argument < argc; ++argument)
    if (!AppendAnimation(output, argv[argument], joint_count)) return 1;

  std::cout << "Wrote " << argv[1] << " with " << argc - 3 << " clips.\n";
}
