#include <cstdint>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

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

}  // namespace

int main(int argc, const char** argv) {
  if (argc < 4) {
    std::cerr << "Usage: myth_rig_pack <output.ozzpack> <skeleton.ozz> <animation.ozz>...\n";
    return 1;
  }

  std::ofstream output(argv[1], std::ios::binary);
  if (!output) {
    std::cerr << "Unable to create " << argv[1] << ".\n";
    return 1;
  }
  WriteU32(output, 0x50525a4f);  // OZRP
  WriteU32(output, 1);
  if (!Append(output, argv[2])) return 1;
  WriteU32(output, static_cast<uint32_t>(argc - 3));
  for (int argument = 3; argument < argc; ++argument)
    if (!Append(output, argv[argument])) return 1;

  std::cout << "Wrote " << argv[1] << " with " << argc - 3 << " clips.\n";
}
