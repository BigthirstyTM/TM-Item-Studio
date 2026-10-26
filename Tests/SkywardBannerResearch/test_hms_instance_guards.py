"""Offline safeguards for native main/staged instance traversal. No sockets."""
import copy
import struct
import unittest

import sample_hms_instance_timing as sampler
from sample_paired_clocks import Refusal


class FakeBridge:
    def __init__(self):
        self.last_span = {"offline": True}
        self.mode = 1
        self.mapping = struct.pack("<3I", 0, 0x80000000, 0x7FFFFFFF)
        self.mapping_reads = 0
        self.change_mapping = False
        self.descriptors = {0x60: {"data": 0x40000, "count": 1, "capacity": 1},
                            0xC0: {"data": 0x41000, "count": 1, "capacity": 1},
                            0xE0: {"data": 0x42000, "count": 1, "capacity": 1},
                            0x1D8: {"data": 0x43000, "count": 3, "capacity": 3},
                            0x1E8: {"data": 0x44000, "count": 1, "capacity": 1},
                            0x208: {"data": 0x45000, "count": 1, "capacity": 1}}
        model = bytearray(0x78)
        struct.pack_into("<Q", model, 0, 0x50000)
        struct.pack_into("<Q", model, 0x10, 0x51000)
        struct.pack_into("<I", model, 0x58, 2)
        self.memory = {0x40000: bytes(model), 0x41000: self.instance(1637),
                       0x42000: struct.pack("<I", 0), 0x44000: self.instance(1636),
                       0x45000: struct.pack("<I", 0x800000)}

    @staticmethod
    def instance(period_code):
        return struct.pack("<7fI", 0, 0, 0, 1, 788, 128, 450, 0x80000000 | period_code)

    def ptr(self, pointer, offset=0):
        assert pointer == 0x10000 and offset == 0x248
        return 0x20000

    def descriptor(self, pointer, offset, maximum):
        assert pointer == 0x20000
        value = copy.deepcopy(self.descriptors[offset])
        if value["count"] > maximum:
            raise Refusal("offline count bound")
        return value

    def read(self, pointer, offset, size):
        if pointer == 0x20000:
            assert offset == 0x30 and size == 4
            return struct.pack("<I", self.mode)
        assert offset == 0
        if pointer == 0x43000:
            self.mapping_reads += 1
            data = self.mapping
            if self.change_mapping and self.mapping_reads > 1:
                data = struct.pack("<3I", 0, 0x80000001, 0x7FFFFFFF)
        else:
            data = self.memory[pointer]
        assert len(data) == size
        return data


class NativeInstanceGuardTests(unittest.TestCase):
    roots = {"zone": 0x10000, "hmsInstances": 0x20000}

    def test_main_staged_and_dead_id_are_distinct(self):
        value = sampler.snapshot(FakeBridge(), self.roots)
        a, b, dead = value["instances"]
        self.assertEqual((a["storage"], a["packedTiming"]["periodCode"]), ("mainC0", 1637))
        self.assertEqual((b["storage"], b["packedTiming"]["periodCode"]), ("staged1E8", 1636))
        self.assertEqual(b["modelIndex"], 0)
        self.assertTrue(dead["deadStableId"])

    def test_out_of_bounds_staged_mapping_is_refused(self):
        bridge = FakeBridge()
        bridge.mapping = struct.pack("<3I", 0, 0x80000001, 0x7FFFFFFF)
        with self.assertRaisesRegex(Refusal, "beyond"):
            sampler.snapshot(bridge, self.roots)

    def test_changed_mapping_is_refused(self):
        bridge = FakeBridge()
        bridge.change_mapping = True
        with self.assertRaisesRegex(Refusal, "mapping changed"):
            sampler.snapshot(bridge, self.roots)

    def test_unknown_storage_mode_is_refused(self):
        bridge = FakeBridge()
        bridge.mode = 2
        with self.assertRaisesRegex(Refusal, "two code-derived"):
            sampler.snapshot(bridge, self.roots)


if __name__ == "__main__":
    unittest.main()
