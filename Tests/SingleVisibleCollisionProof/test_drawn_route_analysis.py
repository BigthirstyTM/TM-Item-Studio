import math
import unittest
from drawn_route_analysis import analyze


class DrawnRouteAnalysisTests(unittest.TestCase):
    def test_reconstructs_non_native_route_without_claiming_export(self):
        points = [[0,0],[17,4],[9,23],[-7,11],[0,0]]
        for heading in (0,37,90,183):
            r = analyze(points,heading_degrees=heading)
            self.assertLess(r['maxReconstructionError'],1e-10)
            self.assertFalse(r['nativeExportSupported'])
            self.assertEqual(r['signals'][0]['translation'],r['signals'][-1]['translation'])
            self.assertEqual(r['signals'][0]['angleDegrees'],r['signals'][-1]['angleDegrees'])

    def test_exact_straight_line_has_constant_angle(self):
        r = analyze([[0,0],[32,0]])
        self.assertAlmostEqual(r['rotation']['min'],r['rotation']['max'])
        self.assertAlmostEqual(r['translation']['max']-r['translation']['min'],32)

    def test_square_needs_compensation_even_on_transverse_edges(self):
        r = analyze([[0,0],[32,0],[32,24],[0,24],[0,0]],radius=15)
        # Halfway across the vertical edge, maintaining x=32 requires the
        # slider to compensate for the changing rotational X component.
        signal = r['signals'][24]
        self.assertAlmostEqual(signal['translation'],17)
        self.assertAlmostEqual(signal['angleDegrees'],90)
        self.assertGreater(r['translation']['sampledInteriorExtrema'],0)

    def test_more_than_eight_input_points(self):
        points = [[i,3*math.sin(i)] for i in range(25)]
        r = analyze(points)
        self.assertEqual(r['inputPointCount'],25)
        self.assertGreater(r['rotation']['sampledDirectionChanges'],4)
        self.assertLess(r['maxReconstructionError'],1e-10)

    def test_rejects_invalid_and_singular_inputs(self):
        for points in ([[0,0]],[[0,0],[0,0]],[[0,0],[float('nan'),1]],[[0,0],[1,2,3]]):
            with self.assertRaises(ValueError):
                analyze(points)
        for radius in (0,-1,12,float('inf')):
            with self.assertRaises(ValueError):
                analyze([[0,0],[32,24]],radius=radius)


if __name__ == '__main__':
    unittest.main()
