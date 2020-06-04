﻿using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Utilities;

namespace NetTopologySuite.Operation.OverlayNg
{
    /// <summary>
    /// Utility methods for overlay processing.
    /// </summary>
    /// <author>Martin Davis</author>
    internal static class OverlayUtility
    {

        private const int SafeEnvExpandFactor = 3;

        static double ExpandDistance(Envelope env, PrecisionModel pm)
        {
            double envExpandDist;
            if (pm.IsFloating)
            {
                // if PM is FLOAT then there is no scale factor, so add 10%
                double minSize = Math.Min(env.Height, env.Width);
                envExpandDist = 0.1 * minSize;
            }
            else
            {
                // if PM is fixed, add a small multiple of the grid size
                double gridSize = 1.0 / pm.Scale;
                envExpandDist = SafeEnvExpandFactor * gridSize;
            }
            return envExpandDist;
        }

        static Envelope SafeOverlapEnv(Envelope env, PrecisionModel pm)
        {
            double envExpandDist = ExpandDistance(env, pm);
            var safeEnv = env.Copy();
            safeEnv.ExpandBy(envExpandDist);
            return safeEnv;
        }

        internal static Envelope ClippingEnvelope(SpatialFunction opCode, InputGeometry inputGeom, PrecisionModel pm)
        {
            Envelope clipEnv = null;
            switch (opCode)
            {
                case OverlayNG.INTERSECTION:
                    var envA = SafeOverlapEnv(inputGeom.GetEnvelope(0), pm);
                    var envB = SafeOverlapEnv(inputGeom.GetEnvelope(1), pm);
                    clipEnv = envA.Intersection(envB);
                    break;
                case OverlayNG.DIFFERENCE:
                    clipEnv = SafeOverlapEnv(inputGeom.GetEnvelope(0), pm);
                    break;
            }
            return clipEnv;
        }

        /// <summary>
        /// Tests if the result can be determined to be empty
        /// based on simple properties of the input geometries
        /// (such as whether one or both are empty, 
        /// or their envelopes are disjoint).
        /// </summary>
        /// <param name="opCode">The overlay operation</param>
        /// <param name="a">The A operand geometry</param>
        /// <param name="b">The B operand geometry</param>
        /// <param name="pm">The precision model to use</param>
        /// <returns><c>true</c> if the overlay result is determined to be empty</returns>
        internal static bool IsEmptyResult(SpatialFunction opCode, Geometry a, Geometry b, PrecisionModel pm)
        {
            switch (opCode)
            {
                case OverlayNG.INTERSECTION:
                    if (IsEnvDisjoint(a, b, pm))
                        return true;
                    break;
                case OverlayNG.DIFFERENCE:
                    if (IsEmpty(a))
                        return true;
                    break;
                case OverlayNG.UNION:
                case OverlayNG.SYMDIFFERENCE:
                    if (IsEmpty(a) && IsEmpty(b))
                        return true;
                    break;
            }
            return false;
        }

        private static bool IsEmpty(Geometry geom)
        {
            return geom == null || geom.IsEmpty;
        }

        /// <summary>
        /// Tests if the geometry envelopes are disjoint, or empty.
        /// The disjoint test must take into account the precision model
        /// being used, since geometry coordinates may shift under rounding.
        /// </summary>
        /// <param name="a">The A operand geometry</param>
        /// <param name="b">The B operand geometry</param>
        /// <param name="pm">The precision model to use</param>
        /// <returns><c>true</c> if the geometry envelopes are disjoint or empty</returns>
        private static bool IsEnvDisjoint(Geometry a, Geometry b, PrecisionModel pm)
        {
            if (IsEmpty(a) || IsEmpty(b)) return true;
            if (pm.IsFloating)
            {
                return a.EnvelopeInternal.Disjoint(b.EnvelopeInternal);
            }
            return IsDisjoint(a.EnvelopeInternal, b.EnvelopeInternal, pm);
        }

        /// <summary>
        /// Tests for disjoint envelopes adjusting for rounding
        /// caused by a fixed precision model.
        /// Assumes envelopes are non-empty.
        /// </summary>
        /// <param name="envA">The A operand envelope</param>
        /// <param name="envB">The B operand envelope</param>
        /// <param name="pm">The precision model to use</param>
        /// <returns><c>true</c> if the envelopes are disjoint</returns>
        private static bool IsDisjoint(Envelope envA, Envelope envB, PrecisionModel pm)
        {
            if (pm.MakePrecise(envB.MinX) > pm.MakePrecise(envA.MaxX)) return true;
            if (pm.MakePrecise(envB.MaxX) < pm.MakePrecise(envA.MinX)) return true;
            if (pm.MakePrecise(envB.MinY) > pm.MakePrecise(envA.MaxY)) return true;
            if (pm.MakePrecise(envB.MaxY) < pm.MakePrecise(envA.MinY)) return true;
            return false;
        }

        /// <summary>
        /// Creates an empty result geometry of the appropriate dimension,
        /// based on the given overlay operation and the dimensions of the inputs.
        /// The created geometry is always an atomic geometry, 
        /// not a collection.
        /// <para/>
        /// The empty result is constructed using the following rules:
        /// <list type="bullet">
        /// <item><term><see cref="SpatialFunction.Intersection"/></term><description>result has the dimension of the lowest input dimension</description></item>
        /// <item><term><see cref="SpatialFunction.Union"/></term><description>result has the dimension of the highest input dimension</description></item>
        /// <item><term><see cref="SpatialFunction.Difference"/></term><description>result has the dimension of the left-hand input</description></item>
        /// <item><term><see cref="SpatialFunction.SymDifference"/></term><description>result has the dimension of the highest input dimension
        /// (since the Symmetric Difference is the Union of the Differences).</description></item>
        /// </list>
        /// </summary>
        /// <param name="dim">The dimension of the empty geometry</param>
        /// <param name="geomFact">The geometry factory being used for the operation</param>
        /// <returns>An empty atomic geometry of the appropriate dimension</returns>
        internal static Geometry CreateEmptyResult(Dimension dim, GeometryFactory geomFact)
        {
            Geometry result = null;
            switch (dim)
            {
                case Dimension.Point:
                    result = geomFact.CreatePoint();
                    break;
                case Dimension.Curve:
                    result = geomFact.CreateLineString();
                    break;
                case Dimension.Surface:
                    result = geomFact.CreatePolygon();
                    break;
                default:
                    Assert.ShouldNeverReachHere("Unable to determine overlay result geometry dimension");
                    break;
            }
            return result;
        }

        /// <summary>
        /// Computes the dimension of the result of
        /// applying the given operation to inputs
        /// with the given dimensions.
        /// This assumes that complete collapse does not occur.
        /// </summary>
        /// <param name="opCode">The overlay operation</param>
        /// <param name="dim0">Dimension of the LH input</param>
        /// <param name="dim1">Dimension of the RH input</param>
        /// <returns></returns>
        internal static Dimension ResultDimension(SpatialFunction opCode, Dimension dim0, Dimension dim1)
        {
            var resultDimension = Dimension.False;
            switch (opCode)
            {
                case OverlayNG.INTERSECTION:
                    resultDimension = (Dimension)Math.Min((int)dim0, (int)dim1);
                    break;
                case OverlayNG.UNION:
                    resultDimension = (Dimension)Math.Max((int)dim0, (int)dim1);
                    break;
                case OverlayNG.DIFFERENCE:
                    resultDimension = dim0;
                    break;
                case OverlayNG.SYMDIFFERENCE:
                    /*
                     * This result is chosen because
                     * <pre>
                     * SymDiff = Union( Diff(A, B), Diff(B, A) )
                     * </pre>
                     * and Union has the dimension of the highest-dimension argument.
                     */
                    resultDimension = (Dimension)Math.Max((int)dim0, (int)dim1);
                    break;
            }
            return resultDimension;
        }

        /// <summary>
        /// Creates an overlay result geometry for homogeneous or mixed components.
        /// </summary>
        /// <param name="resultPolyList">An enumeration of result polygons (may be empty or <c>null</c>)</param>
        /// <param name="resultLineList">An enumeration of result lines (may be empty or <c>null</c>)</param>
        /// <param name="resultPointList">An enumeration of result points (may be empty or <c>null</c>)</param>
        /// <param name="geometryFactory">The geometry factory to use.</param>
        /// <returns>A geometry structured according to the overlay result semantics</returns>
        internal static Geometry CreateResultGeometry(IEnumerable<Polygon> resultPolyList, IEnumerable<LineString> resultLineList, IEnumerable<Point> resultPointList, GeometryFactory geometryFactory)
        {
            var geomList = new List<Geometry>();

            // TODO: for mixed dimension, return collection of Multigeom for each dimension (breaking change)

            // element geometries of the result are always in the order A,L,P
            if (resultPolyList != null) geomList.AddRange(resultPolyList);
            if (resultLineList != null) geomList.AddRange(resultLineList);
            if (resultPointList != null) geomList.AddRange(resultPointList);

            // build the most specific geometry possible
            // TODO: perhaps do this internally to give more control?
            return geometryFactory.BuildGeometry(geomList);
        }

        internal static Geometry ToLines(OverlayGraph graph, bool isOutputEdges, GeometryFactory geomFact)
        {
            var lines = new List<LineString>();
            foreach (var edge in graph.Edges)
            {
                bool includeEdge = isOutputEdges || edge.IsInResultArea;
                if (!includeEdge) continue;
                //Coordinate[] pts = getCoords(nss);
                var pts = edge.CoordinatesOriented;
                var line = geomFact.CreateLineString(pts);
                line.UserData = LabelForResult(edge);
                lines.Add(line);
            }
            return geomFact.BuildGeometry(lines);
        }

        private static string LabelForResult(OverlayEdge edge)
        {
            return edge.Label.ToString(edge.IsForward)
                + (edge.IsInResultArea ? " Res" : "");
        }

        /// <summary>
        /// Round the key point if precision model is fixed.
        /// Note: return value is only copied if rounding is performed.
        /// </summary>
        /// <param name="pt">The point to round</param>
        /// <param name="pm">The precision model to use</param>
        /// <returns>The rounded point coordinate, or null if empty</returns>
        public static Coordinate Round(Point pt, PrecisionModel pm)
        {
            if (pt.IsEmpty) return null;
            var p = pt.Coordinate.Copy();
            if (!pm.IsFloating)
                pm.MakePrecise(p);
            return p;
        }

        /*
        private void checkSanity(Geometry result) {
          // for Union, area should be greater than largest of inputs
          double areaA = inputGeom.getGeometry(0).getArea();
          double areaB = inputGeom.getGeometry(1).getArea();
          double area = result.getArea();

          // if result is empty probably had a complete collapse, so can't use this check
          if (area == 0) return;

          if (opCode == UNION) {
            double minAreaLimit = 0.5 * Math.max(areaA, areaB);
            if (area < minAreaLimit ) {
              throw new TopologyException("Result area sanity issue");
            }
          }
        }
      */

    }

}