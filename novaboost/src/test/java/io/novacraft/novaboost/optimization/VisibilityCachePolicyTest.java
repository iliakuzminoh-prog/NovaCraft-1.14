package io.novacraft.novaboost.optimization;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.UUID;

import org.junit.jupiter.api.Test;

class VisibilityCachePolicyTest {
    private static final UUID ENTITY_ID = UUID.fromString("10e3a79a-547f-4f91-a089-2e5e3e3a11b8");

    private static boolean canReuse(
        UUID cachedId,
        UUID currentId,
        long expiresAt,
        long tick,
        double cameraX,
        double targetX
    ) {
        return VisibilityCachePolicy.canReuse(
            cachedId,
            currentId,
            expiresAt,
            tick,
            0.0D, 64.0D, 0.0D,
            cameraX, 64.0D, 0.0D,
            10.0D, 64.0D, 10.0D,
            targetX, 64.0D, 10.0D
        );
    }

    @Test
    void reusesResultWhenIdentityAndPositionsAreStable() {
        assertTrue(canReuse(ENTITY_ID, ENTITY_ID, 12L, 10L, 0.1D, 10.1D));
    }

    @Test
    void rejectsExpiredResult() {
        assertFalse(canReuse(ENTITY_ID, ENTITY_ID, 9L, 10L, 0.0D, 10.0D));
    }

    @Test
    void rejectsReusedRuntimeEntityIdWithDifferentUuid() {
        assertFalse(canReuse(ENTITY_ID, UUID.randomUUID(), 12L, 10L, 0.0D, 10.0D));
    }

    @Test
    void rejectsCacheAfterCameraMovesTooFar() {
        assertFalse(canReuse(ENTITY_ID, ENTITY_ID, 12L, 10L, 1.0D, 10.0D));
    }

    @Test
    void rejectsCacheAfterTargetMovesTooFar() {
        assertFalse(canReuse(ENTITY_ID, ENTITY_ID, 12L, 10L, 0.0D, 10.5D));
    }

    @Test
    void allowsVerySmallCameraAndTargetMovement() {
        assertTrue(canReuse(ENTITY_ID, ENTITY_ID, 12L, 10L, 0.2D, 10.2D));
    }
}
