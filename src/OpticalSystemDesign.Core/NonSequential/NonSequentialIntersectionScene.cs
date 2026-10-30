using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.NonSequential;

public sealed record NonSequentialSceneIntersection(Guid ObjectId, double DistanceMillimeters,
    Vector3D Position, Vector3D Normal, Vector3D LocalPosition, int FaceNumber);

public sealed partial class NonSequentialDocumentTracer
{
    /// <summary>Read-only spatial query over the same compiled geometries/BVH used by the formal NSC tracer.
    /// No optical interaction, ray splitting, detector accumulation or document mutation occurs.</summary>
    public static IntersectionScene CompileIntersections(NonSequentialDocument document) => new(document);

    public sealed class IntersectionScene
    {
        private readonly NonSequentialDocument _document;
        private readonly BvhNode? _bvh;

        internal IntersectionScene(NonSequentialDocument document)
        {
            ArgumentNullException.ThrowIfNull(document); ComputationCancellation.ThrowIfCancellationRequested();
            _document = document.Clone();
            var objects = _document.Objects.Where(o => o.Enabled && !IsSource(o.Kind)).ToArray();
            _bvh = BvhNode.Build(objects.Select(o => BoundedObject.Create(_document, o)).ToArray());
        }

        public NonSequentialSceneIntersection? Intersect(RealRay ray, double maximumDistanceMillimeters = double.PositiveInfinity)
        {
            ArgumentNullException.ThrowIfNull(ray); ComputationCancellation.ThrowIfCancellationRequested();
            if (!ray.CanTrace || !double.IsFinite(ray.Direction.Length) || double.IsNaN(maximumDistanceMillimeters)
                || maximumDistanceMillimeters <= 0) throw new ArgumentException("Invalid scene query ray or distance.");
            var hit = FindNearestHit(_document, _bvh, ray.Normalize());
            return hit is null || hit.Distance > maximumDistanceMillimeters ? null
                : new(hit.Item.Id, hit.Distance, hit.WorldPoint, hit.WorldNormal, hit.LocalPoint, hit.FaceNumber);
        }

        public NonSequentialSceneIntersection? IntersectObject(Guid objectId, RealRay ray)
        {
            ArgumentNullException.ThrowIfNull(ray); ComputationCancellation.ThrowIfCancellationRequested();
            if (!ray.CanTrace || !double.IsFinite(ray.Direction.Length)) throw new ArgumentException("Invalid scene query ray.");
            if (!_document.TryFindObject(objectId, out var item)) throw new ArgumentException("Unknown scene object.");
            var hit = item.Enabled ? NonSequentialDocumentTracer.Intersect(_document, item, ray.Normalize()) : null;
            return hit is null ? null : new(hit.Item.Id, hit.Distance, hit.WorldPoint, hit.WorldNormal, hit.LocalPoint, hit.FaceNumber);
        }
    }
}
