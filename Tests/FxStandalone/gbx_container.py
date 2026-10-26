"""Bounded GBX6 binary container access without deserializing the item graph.

The header and reference table are retained exactly. Only the decompressed body
is edited; compressed LZO bodies are always decompressed before searching.
"""

from __future__ import annotations

from dataclasses import dataclass
import struct

import gbx.lzo


MAX_BODY = 256 * 1024 * 1024


@dataclass(frozen=True)
class GbxContainer:
    prefix: bytes
    body: bytes
    compression: str
    node_count: int
    references: tuple[dict, ...]

    @classmethod
    def read(cls, data: bytes) -> "GbxContainer":
        if len(data) < 25 or data[:7] != b"GBX\x06\x00BU" or data[7:8] not in (b"C", b"U"):
            raise ValueError("expected GBX6 binary data with an uncompressed reference table")
        position = 13

        def integer() -> int:
            nonlocal position
            if position + 4 > len(data):
                raise ValueError("truncated GBX container integer")
            value = struct.unpack_from("<I", data, position)[0]
            position += 4
            return value

        def string() -> str:
            nonlocal position
            length = integer()
            if length > 1024 * 1024 or position + length > len(data):
                raise ValueError("invalid GBX reference-table string length")
            value = data[position:position + length].decode("utf-8")
            position += length
            return value

        header_size = integer()
        if header_size > len(data) - position:
            raise ValueError("truncated GBX user-data header")
        position += header_size
        node_count = integer()
        external_count = integer()
        if node_count > 10_000_000 or external_count > 1_000_000:
            raise ValueError("GBX node count exceeds bounded patch scope")
        folders = [""]
        ancestor = 0
        if external_count:
            ancestor = integer()

            def read_folders(parent: str, depth: int = 0) -> None:
                if depth > 128:
                    raise ValueError("GBX reference folder tree exceeds bounded patch scope")
                count = integer()
                if count > 100_000:
                    raise ValueError("invalid GBX folder count")
                for _ in range(count):
                    name = string()
                    path = parent + name + "\\"
                    folders.append(path)
                    read_folders(path, depth + 1)

            read_folders("")
        references = []
        for _ in range(external_count):
            start = position
            flags = integer()
            resource = bool(flags & 4)
            ref = integer() if resource else string()
            node_index = integer()
            use_file = integer()
            folder_index = None if resource else integer()
            if not resource and folder_index >= len(folders):
                raise ValueError("invalid GBX reference folder index")
            references.append({"flags": flags, "ref": ref, "nodeIndex": node_index,
                               "useFile": bool(use_file), "folderIndex": folder_index,
                               "ancestorLevel": ancestor, "start": start, "end": position,
                               "path": None if resource else folders[folder_index] + ref})
        prefix = data[:position]
        compression = chr(data[7])
        if compression == "C":
            uncompressed_size = integer()
            compressed_size = integer()
            if not 0 < uncompressed_size <= MAX_BODY or compressed_size != len(data) - position:
                raise ValueError("invalid or trailing compressed GBX body")
            body = gbx.lzo.decompress(data[position:], False, uncompressed_size)
            if len(body) != uncompressed_size:
                raise ValueError("LZO output length does not match the declared body length")
        else:
            body = data[position:]
            if not 0 < len(body) <= MAX_BODY:
                raise ValueError("uncompressed body exceeds bounded patch scope")
        return cls(prefix, body, compression, node_count, tuple(references))

    def write(self, body: bytes) -> bytes:
        if not 0 < len(body) <= MAX_BODY:
            raise ValueError("patched body exceeds bounded patch scope")
        if self.compression == "U":
            result = self.prefix + body
        else:
            compressed = gbx.lzo.compress(body, 1, False)
            result = self.prefix + struct.pack("<II", len(body), len(compressed)) + compressed
        reopened = self.read(result)
        if reopened.prefix != self.prefix or reopened.body != body:
            raise ValueError("GBX container write did not preserve the prefix and body")
        return result
