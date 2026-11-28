package io.novacraft.novaboost.config;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Properties;

import net.fabricmc.loader.api.FabricLoader;

import io.novacraft.novaboost.NovaBoost;

public final class NovaBoostConfig {
    private static volatile boolean entityOcclusionEnabled = true;
    private static volatile int raycastsPerTick = 24;
    private static volatile int cacheTicks = 4;
    private static volatile int minimumDistance = 8;
    private static volatile int maximumDistance = 80;

    private NovaBoostConfig() {
    }

    public static void load() {
        Path configPath = FabricLoader.getInstance().getConfigDir().resolve("novaboost.properties");
        Properties properties = new Properties();
        properties.setProperty("entity-occlusion", "true");
        properties.setProperty("raycasts-per-tick", "24");
        properties.setProperty("cache-ticks", "2");
        properties.setProperty("minimum-distance", "8");
        properties.setProperty("maximum-distance", "80");

        try {
            if (Files.exists(configPath)) {
                try (InputStream input = Files.newInputStream(configPath)) {
                    properties.load(input);
                }
            } else {
                Files.createDirectories(configPath.getParent());
                Path temporary = configPath.resolveSibling(configPath.getFileName() + ".tmp");
                try (OutputStream output = Files.newOutputStream(temporary)) {
                    properties.store(output, "NovaBoost client optimization settings");
                }
                try {
                    Files.move(temporary, configPath, StandardCopyOption.ATOMIC_MOVE);
                } catch (IOException moveFailure) {
                    Files.move(temporary, configPath, StandardCopyOption.REPLACE_EXISTING);
                }
            }

            entityOcclusionEnabled = Boolean.parseBoolean(properties.getProperty("entity-occlusion", "true"));
            raycastsPerTick = clamp(parseInt(properties, "raycasts-per-tick", 24), 6, 96);
            cacheTicks = clamp(parseInt(properties, "cache-ticks", 2), 1, 3);
            minimumDistance = clamp(parseInt(properties, "minimum-distance", 8), 4, 32);
            maximumDistance = clamp(parseInt(properties, "maximum-distance", 80), 16, 192);

            if (maximumDistance <= minimumDistance) {
                maximumDistance = Math.min(192, minimumDistance + 16);
            }
        } catch (IOException | SecurityException exception) {
            NovaBoost.LOGGER.warn("Could not read or create novaboost.properties; safe defaults remain active.", exception);
        }
    }

    public static boolean entityOcclusionEnabled() {
        return entityOcclusionEnabled;
    }

    public static int raycastsPerTick() {
        return raycastsPerTick;
    }

    public static int cacheTicks() {
        return cacheTicks;
    }

    public static int minimumDistance() {
        return minimumDistance;
    }

    public static int maximumDistance() {
        return maximumDistance;
    }

    private static int parseInt(Properties properties, String key, int fallback) {
        try {
            return Integer.parseInt(properties.getProperty(key, Integer.toString(fallback)).trim());
        } catch (NumberFormatException exception) {
            NovaBoost.LOGGER.warn("Invalid value for '{}'; using {}.", key, fallback);
            return fallback;
        }
    }

    private static int clamp(int value, int minimum, int maximum) {
        return Math.max(minimum, Math.min(maximum, value));
    }
}
