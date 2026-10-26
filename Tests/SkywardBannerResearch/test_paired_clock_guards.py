"""Offline negative controls for the read-only sampler; never contacts the game."""
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

import sample_paired_clocks as sampler


class FakeSocket:
    def __init__(self, response):
        self.response = json.dumps(response).encode() + b"\n"

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False

    def settimeout(self, value):
        pass

    def sendall(self, value):
        self.request = json.loads(value)

    def shutdown(self, value):
        pass

    def recv(self, size):
        result, self.response = self.response[:size], self.response[size:]
        return result


class ReadGuards(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="tmis-paired-clock-offline-")
        self.bridge = sampler.Bridge(Path(self.directory.name), 15)

    def tearDown(self):
        self.bridge.trace.close()
        self.directory.cleanup()

    def response(self, output):
        return FakeSocket({"ok": True, "data": {"result": {"success": True, "output": output}}})

    def test_mutation_tool_refused_before_any_socket(self):
        with patch.object(sampler.socket, "create_connection", side_effect=AssertionError("Network must not be reached")):
            with self.assertRaises(sampler.Refusal):
                self.bridge.call("DevSafeWrite", {"ptr": "0x10000", "value": 1})

    def test_read_error_in_success_envelope_is_still_failure(self):
        fake = self.response({"addr": "0x10000", "probe": {"readable": True}, "readError": "fault"})
        with patch.object(sampler.socket, "create_connection", return_value=fake):
            with self.assertRaisesRegex(sampler.Refusal, "Read refused"):
                self.bridge.read(0x10000, 0, 4)

    def test_truncated_byte_dump_refused(self):
        fake = self.response({"addr": "0x10000", "probe": {"readable": True},
                              "dump": {"readBytes": 3, "truncatedAt": 3, "bytes": [1, 2, 3]}})
        with patch.object(sampler.socket, "create_connection", return_value=fake):
            with self.assertRaisesRegex(sampler.Refusal, "Incomplete"):
                self.bridge.read(0x10000, 0, 4)

    def test_wrong_echoed_address_refused(self):
        fake = self.response({"addr": "0x10008", "probe": {"readable": True},
                              "dump": {"readBytes": 4, "bytes": [1, 2, 3, 4]}})
        with patch.object(sampler.socket, "create_connection", return_value=fake):
            with self.assertRaisesRegex(sampler.Refusal, "address mismatch"):
                self.bridge.read(0x10000, 0, 4)

    def test_oversized_registration_list_refused_before_following_data(self):
        with patch.object(self.bridge, "read", return_value=struct.pack("<QII", 0x20000, 9, 16)) as read:
            with self.assertRaisesRegex(sampler.Refusal, "exceeds fixture bound"):
                self.bridge.descriptor(0x10000, 8, 8)
            self.assertEqual(read.call_count, 1)

    def test_valid_exact_pointer_read_retains_64_bit_value(self):
        expected = 0x00007FFE12345678
        fake = self.response({"addr": "0x10008", "probe": {"readable": True},
                              "dump": {"readBytes": 8, "bytes": list(struct.pack("<Q", expected))}})
        with patch.object(sampler.socket, "create_connection", return_value=fake):
            self.assertEqual(self.bridge.ptr(0x10000, 8), expected)
            self.assertEqual(fake.request["tool"], "DevSafeRead")
        self.bridge.trace.flush()
        records = [json.loads(line) for line in (Path(self.directory.name) / "requests.jsonl").read_text().splitlines()]
        self.assertEqual([record["event"] for record in records], ["request", "response"])


if __name__ == "__main__":
    unittest.main()
