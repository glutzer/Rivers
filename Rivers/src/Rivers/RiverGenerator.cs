using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

public class RiverGenerator
{
    public double riverDepth;
    public double baseDepth;

    public Noise riverDistortionX;
    public Noise riverDistortionZ;

    public RiverGenerator()
    {
        riverDepth = RiverConfig.Loaded.riverDepth;
        baseDepth = RiverConfig.Loaded.baseDepth;

        riverDistortionX = new Noise(0, 0.005f, 2, 0.4f, 2.6f);
        riverDistortionZ = new Noise(2, 0.005f, 2, 0.4f, 2.6f);
    }

    public RiverSample SampleRiver(List<RiverSegment> segments, double x, double z)
    {
        RiverSample riverSample = new();

        double closestLine = double.PositiveInfinity;

        double distX = riverDistortionX.GetNoise(x, z);
        double distZ = riverDistortionZ.GetNoise(x, z);

        Vec2d point = new(x + distX * 10, z + distZ * 10);

        foreach (RiverSegment segment in segments)
        {
            float riverProjection = RiverMath.GetProjection(point, segment.river.startPoint, segment.river.endPoint); //How far along the main river for size calculation

            double riverSize = GameMath.Lerp(segment.river.startSize, segment.river.endSize, riverProjection); //Size of river

            float segmentProjection = RiverMath.GetProjection(point, segment.startPoint, segment.endPoint);

            List<RiverSegment> connectors = new();
            if (segmentProjection > 0.5)
            {
                foreach (RiverSegment child in segment.children)
                {
                    connectors.Add(child);
                }
            }
            else
            {
                connectors.Add(segment.parent);
            }

            foreach (RiverSegment connector in connectors)
            {
                Vec2d lerpedStart = new();
                Vec2d lerpedEnd = new();

                float midPointProjection;
                if (segment.midPoint != connector.midPoint)
                {
                    midPointProjection = RiverMath.GetProjection(point, segment.midPoint, connector.midPoint);
                }
                else
                {
                    midPointProjection = 1;
                }

                //Projections onto lines with equal length are NaN
                if (segmentProjection > 0.5)
                {
                    if (connector.parentInvalid) midPointProjection = 0;

                    lerpedStart.X = GameMath.Lerp(segment.midPoint.X, connector.startPoint.X, midPointProjection);
                    lerpedStart.Y = GameMath.Lerp(segment.midPoint.Y, connector.startPoint.Y, midPointProjection);
                    lerpedEnd.X = GameMath.Lerp(segment.endPoint.X, connector.midPoint.X, midPointProjection);
                    lerpedEnd.Y = GameMath.Lerp(segment.endPoint.Y, connector.midPoint.Y, midPointProjection);
                }
                else
                {
                    if (segment.parentInvalid) midPointProjection = 0;

                    lerpedStart.X = GameMath.Lerp(segment.startPoint.X, connector.midPoint.X, midPointProjection);
                    lerpedStart.Y = GameMath.Lerp(segment.startPoint.Y, connector.midPoint.Y, midPointProjection);
                    lerpedEnd.X = GameMath.Lerp(segment.midPoint.X, connector.endPoint.X, midPointProjection);
                    lerpedEnd.Y = GameMath.Lerp(segment.midPoint.Y, connector.endPoint.Y, midPointProjection);
                }

                double distance = RiverMath.DistanceToLine(point, lerpedStart, lerpedEnd);

                //Calculate bank factor
                if (distance <= riverSize) //If within bank distance
                {
                    if (distance < closestLine)
                    {
                        //If a bank exists, the flow vector is the same as it
                        Vec2d segmentFlowVector = lerpedStart - lerpedEnd;
                        segmentFlowVector.Normalize();

                        //Round the flow to group together sets of water
                        segmentFlowVector.X = Math.Round(segmentFlowVector.X, 1);
                        segmentFlowVector.Y = Math.Round(segmentFlowVector.Y, 1);

                        segmentFlowVector.Normalize();

                        //If it's not moving don't add it
                        if (segment.river.speed > 0)
                        {
                            riverSample.flowVectorX = (float)segmentFlowVector.X * segment.river.speed;
                            riverSample.flowVectorZ = (float)segmentFlowVector.Y * segment.river.speed;
                        }
                        
                        closestLine = distance;
                    }

                    riverSample.riverDistance = 0;

                    double lerp = 1 - RiverMath.InverseLerp(distance, riverSize, 0);

                    riverSample.bankFactor = Math.Max(Math.Max(Math.Sqrt(riverSize) * riverDepth, baseDepth) * (1 - lerp * lerp), riverSample.bankFactor); //Deepest bank

                    continue;
                }

                distance -= riverSize;

                if (distance < 0) distance = 0;

                riverSample.riverDistance = Math.Min(distance, riverSample.riverDistance); //Lowest distance to the edge of a river
            }
        }

        return riverSample;
    }
}

public struct RiverSample
{
    public double riverDistance;
    public double bankFactor;

    public float flowVectorX;
    public float flowVectorZ;

    public RiverSample()
    {
        riverDistance = 5000;
        bankFactor = 0;
        flowVectorX = -100; //Initialized at -100 for checks. Nothing will move this fast
    }
}