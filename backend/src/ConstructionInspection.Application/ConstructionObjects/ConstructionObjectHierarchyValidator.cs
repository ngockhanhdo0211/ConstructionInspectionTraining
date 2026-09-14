using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Application.ConstructionObjects;

internal static class ConstructionObjectHierarchyValidator
{
    public static async Task ValidateAsync(
        Guid? currentId,
        ConstructionObjectType objectType,
        Guid? parentId,
        IConstructionObjectRepository repository,
        CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            if (!ConstructionObjectRules.IsParentTypeValid(objectType, null))
            {
                throw Conflict(
                    "construction_object.invalid_parent_type",
                    "Only a Project can be a root Construction Object.");
            }

            return;
        }

        if (currentId == parentId)
        {
            throw Conflict(
                "construction_object.self_parent",
                "A Construction Object cannot be its own parent.");
        }

        var candidateParent = await repository.GetHierarchyNodeAsync(parentId.Value, cancellationToken)
            ?? throw new ResourceNotFoundException(
                "construction_object.parent_not_found",
                "The parent Construction Object was not found.");

        if (!candidateParent.IsActive)
        {
            throw Conflict(
                "construction_object.parent_inactive",
                "An inactive Construction Object cannot receive a child.");
        }

        if (!ConstructionObjectRules.IsParentTypeValid(objectType, candidateParent.ObjectType))
        {
            throw Conflict(
                "construction_object.invalid_parent_type",
                "The ObjectType is not compatible with the selected parent.");
        }

        var visited = new HashSet<Guid>();
        var node = candidateParent;
        var depth = 1;
        var traversals = 0;

        while (true)
        {
            traversals++;
            if (traversals > ConstructionObjectRules.HierarchyTraversalSafetyLimit)
            {
                throw Conflict(
                    "construction_object.tree_invalid",
                    "The Construction Object hierarchy exceeded the safety limit.");
            }

            if (node.Id == currentId)
            {
                throw Conflict(
                    "construction_object.cycle_detected",
                    "The requested parent would create a hierarchy cycle.");
            }

            if (!visited.Add(node.Id))
            {
                throw Conflict(
                    "construction_object.tree_invalid",
                    "The existing Construction Object hierarchy contains a cycle.");
            }

            depth++;
            if (depth > ConstructionObjectRules.MaximumHierarchyDepth)
            {
                throw Conflict(
                    "construction_object.maximum_depth_exceeded",
                    "The Construction Object hierarchy cannot exceed five levels.");
            }

            if (node.ParentId is null)
            {
                if (node.ObjectType != ConstructionObjectType.Project)
                {
                    throw Conflict(
                        "construction_object.tree_invalid",
                        "The existing Construction Object hierarchy has a non-Project root.");
                }

                return;
            }

            if (depth == ConstructionObjectRules.MaximumHierarchyDepth)
            {
                throw Conflict(
                    "construction_object.maximum_depth_exceeded",
                    "The Construction Object hierarchy cannot exceed five levels.");
            }

            var ancestor = await repository.GetHierarchyNodeAsync(node.ParentId.Value, cancellationToken)
                ?? throw Conflict(
                    "construction_object.tree_invalid",
                    "The existing Construction Object hierarchy has a missing ancestor.");

            if (ancestor.Id == currentId)
            {
                throw Conflict(
                    "construction_object.cycle_detected",
                    "The requested parent would create a hierarchy cycle.");
            }

            if (!ConstructionObjectRules.IsParentTypeValid(node.ObjectType, ancestor.ObjectType))
            {
                throw Conflict(
                    "construction_object.tree_invalid",
                    "The existing Construction Object hierarchy contains incompatible types.");
            }

            node = ancestor;
        }
    }

    private static ConflictException Conflict(string code, string message) => new(code, message);
}
