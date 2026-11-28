package io.novacraft.novaboost;

import net.fabricmc.api.ClientModInitializer;

import io.novacraft.novaboost.config.NovaBoostConfig;
import io.novacraft.novaboost.optimization.EntityOcclusionCuller;

public final class NovaBoostClient implements ClientModInitializer {
    @Override
    public void onInitializeClient() {
        NovaBoostConfig.load();
        EntityOcclusionCuller.INSTANCE.initialize();

        NovaBoost.LOGGER.info(
            "NovaBoost 0.1.0 loaded (entity occlusion: {}, ray budget: {}/tick)",
            NovaBoostConfig.entityOcclusionEnabled(),
            NovaBoostConfig.raycastsPerTick()
        );
    }
}
