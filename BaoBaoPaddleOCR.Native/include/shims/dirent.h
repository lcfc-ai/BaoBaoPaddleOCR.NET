#pragma once

#ifdef _WIN32

#include <sys/stat.h>
#include <windows.h>

#include <cstring>
#include <memory>
#include <string>

struct dirent {
  char d_name[MAX_PATH + 1];
};

struct DIR {
  HANDLE handle;
  WIN32_FIND_DATAA data;
  dirent entry;
  bool first_read;
  std::string pattern;
};

inline DIR* opendir(const char* dirname) {
  if (dirname == nullptr || *dirname == '\0') {
    return nullptr;
  }

  auto dir = std::make_unique<DIR>();
  dir->handle = INVALID_HANDLE_VALUE;
  dir->first_read = true;
  dir->pattern = dirname;
  if (!dir->pattern.empty()) {
    const auto back = dir->pattern.back();
    if (back != '\\' && back != '/') {
      dir->pattern += "\\*";
    } else {
      dir->pattern += "*";
    }
  }

  dir->handle = FindFirstFileA(dir->pattern.c_str(), &dir->data);
  if (dir->handle == INVALID_HANDLE_VALUE) {
    return nullptr;
  }

  return dir.release();
}

inline dirent* readdir(DIR* dir) {
  if (dir == nullptr || dir->handle == INVALID_HANDLE_VALUE) {
    return nullptr;
  }

  while (true) {
    WIN32_FIND_DATAA current = {};
    if (dir->first_read) {
      current = dir->data;
      dir->first_read = false;
    } else {
      if (!FindNextFileA(dir->handle, &current)) {
        return nullptr;
      }
    }

    std::strncpy(dir->entry.d_name, current.cFileName, MAX_PATH);
    dir->entry.d_name[MAX_PATH] = '\0';
    return &dir->entry;
  }
}

inline int closedir(DIR* dir) {
  if (dir == nullptr) {
    return -1;
  }

  if (dir->handle != INVALID_HANDLE_VALUE) {
    FindClose(dir->handle);
  }

  delete dir;
  return 0;
}

#ifndef S_ISDIR
#define S_ISDIR(mode) (((mode) & _S_IFMT) == _S_IFDIR)
#endif

#endif
