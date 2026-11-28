package io.novacraft.novaboost.optimization;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

final class EntityCullingPolicyTest {
    @Test
    void acceptsOrdinarySizedVanillaEntities() {
        assertTrue(EntityCullingPolicy.isSafeCandidate("minecraft", 0.6D, 1.8D, 0.6D));
        assertTrue(EntityCullingPolicy.isSafeCandidate("minecraft", 1.4D, 2.9D, 1.4D));
    }

    @Test
    void rejectsModProvidedEntityTypes() {
        assertFalse(EntityCullingPolicy.isSafeCandidate("examplemod", 0.6D, 1.8D, 0.6D));
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft_extra", 0.6D, 1.8D, 0.6D));
    }

    @Test
    void rejectsOversizedEntities() {
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", 1.6D, 1.8D, 1.0D));
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", 1.0D, 3.1D, 1.0D));
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", 1.0D, 1.8D, 1.6D));
    }

    @Test
    void rejectsInvalidDimensions() {
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", 0.0D, 1.0D, 1.0D));
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", -1.0D, 1.0D, 1.0D));
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", Double.NaN, 1.0D, 1.0D));
        assertFalse(EntityCullingPolicy.isSafeCandidate("minecraft", 1.0D, Double.POSITIVE_INFINITY, 1.0D));
    }
}
