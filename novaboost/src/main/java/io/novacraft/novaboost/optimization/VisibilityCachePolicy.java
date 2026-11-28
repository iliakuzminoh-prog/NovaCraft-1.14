package io.novacraft.novaboost.optimization;

import java.util.UUID;

/**
 * Pure cache-reuse policy. It never reads Minecraft world or entity objects,
 * so it can be tested independently from the renderer.
 */
public final class VisibilityCachePolicy {
    private static final double MAX_CAMERA_MOVEMENT_SQUARED = 0.75D * 0.75D;
    private static final double MAX_TARGET_MOVEMENT_SQUARED = 0.30D * 0.30D;

    private VisibilityCachePolicy() {
    }

    public static boolean canReuse(
        UUID cachedEntityUuid,
        UUID currentEntityUuid,
        long expiresAt,
        long currentTick,
        double cachedCameraX,
        double cachedCameraY,
        double cachedCameraZ,
        double currentCameraX,
        double currentCameraY,
        double currentCameraZ,
        double cachedTargetX,
        double cachedTargetY,
        double cachedTargetZ,
        double currentTargetX,
        double currentTargetY,
        double currentTargetZ
    ) {
        if (cachedEntityUuid == null
            || currentEntityUuid == null
            || !cachedEntityUuid.equals(currentEntityUuid)
            || currentTick > expiresAt) {
            return false;
        }

        double cameraDx = currentCameraX - cachedCameraX;
        double cameraDy = currentCameraY - cachedCameraY;
        double cameraDz = currentCameraZ - cachedCameraZ;
        if (cameraDx * cameraDx + cameraDy * cameraDy + cameraDz * cameraDz
            > MAX_CAMERA_MOVEMENT_SQUARED) {
            return false;
        }

        double targetDx = currentTargetX - cachedTargetX;
        double targetDy = currentTargetY - cachedTargetY;
        double targetDz = currentTargetZ - cachedTargetZ;
        return targetDx * targetDx + targetDy * targetDy + targetDz * targetDz
            <= MAX_TARGET_MOVEMENT_SQUARED;
    }
}
