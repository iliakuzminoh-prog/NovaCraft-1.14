package io.novacraft.novaboost.optimization;

/**
 * Fail-open eligibility rules for the entity occlusion optimization.
 *
 * Only ordinary-sized vanilla entity types are considered. Unknown and
 * mod-provided entity types are rendered normally unless a future version
 * explicitly adds compatibility rules for them.
 */
public final class EntityCullingPolicy {
    private static final String VANILLA_NAMESPACE = "minecraft";
    private static final double MAX_HORIZONTAL_SIZE = 1.5D;
    private static final double MAX_HEIGHT = 3.0D;

    private EntityCullingPolicy() {
    }

    public static boolean isSafeCandidate(
        String namespace,
        double width,
        double height,
        double depth
    ) {
        return VANILLA_NAMESPACE.equals(namespace)
            && isValidDimension(width)
            && isValidDimension(height)
            && isValidDimension(depth)
            && width <= MAX_HORIZONTAL_SIZE
            && depth <= MAX_HORIZONTAL_SIZE
            && height <= MAX_HEIGHT;
    }

    private static boolean isValidDimension(double value) {
        return Double.isFinite(value) && value > 0.0D;
    }
}
