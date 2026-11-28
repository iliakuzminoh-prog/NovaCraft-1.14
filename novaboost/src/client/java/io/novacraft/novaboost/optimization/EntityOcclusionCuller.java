package io.novacraft.novaboost.optimization;

import java.util.HashMap;
import java.util.Iterator;
import java.util.Map;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Avatar;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

import io.novacraft.novaboost.config.NovaBoostConfig;

public final class EntityOcclusionCuller {
    public static final EntityOcclusionCuller INSTANCE = new EntityOcclusionCuller();

    private static final int SAMPLE_COUNT = 3;
    private static final int CACHE_CAPACITY = 4096;

    private final Map<Integer, CachedVisibility> visibilityCache = new HashMap<>();
    private final RaycastBudget raycastBudget = new RaycastBudget(24);

    private Object currentLevel;
    private long clientTick;

    private EntityOcclusionCuller() {
    }

    public void initialize() {
        ClientTickEvents.START_CLIENT_TICK.register(this::onStartClientTick);
    }

    private void onStartClientTick(Minecraft client) {
        clientTick++;
        raycastBudget.reset(NovaBoostConfig.raycastsPerTick());

        if (client.level != currentLevel) {
            currentLevel = client.level;
            visibilityCache.clear();
        }

        if ((clientTick & 31L) == 0L) {
            Iterator<Map.Entry<Integer, CachedVisibility>> iterator = visibilityCache.entrySet().iterator();
            while (iterator.hasNext()) {
                if (iterator.next().getValue().expiresAt() < clientTick) {
                    iterator.remove();
                }
            }
        }
    }

    public boolean shouldCull(
        Minecraft client,
        Entity entity,
        double cameraX,
        double cameraY,
        double cameraZ
    ) {
        if (!NovaBoostConfig.entityOcclusionEnabled()
            || client.level == null
            || client.player == null
            || entity == client.player
            || entity instanceof Avatar
            || entity.isRemoved()
            || entity.isPassenger()
            || entity.isVehicle()
            || entity.isCurrentlyGlowing()
            || entity.getCustomName() != null) {
            return false;
        }

        if (client.level != currentLevel) {
            currentLevel = client.level;
            visibilityCache.clear();
        }

        AABB bounds = entity.getBoundingBox();
        double centerX = (bounds.minX + bounds.maxX) * 0.5D;
        double centerY = (bounds.minY + bounds.maxY) * 0.5D;
        double centerZ = (bounds.minZ + bounds.maxZ) * 0.5D;
        double dx = centerX - cameraX;
        double dy = centerY - cameraY;
        double dz = centerZ - cameraZ;
        double distanceSquared = dx * dx + dy * dy + dz * dz;

        double minimumDistance = NovaBoostConfig.minimumDistance();
        double maximumDistance = NovaBoostConfig.maximumDistance();
        if (distanceSquared < minimumDistance * minimumDistance
            || distanceSquared > maximumDistance * maximumDistance) {
            return false;
        }

        CachedVisibility cached = visibilityCache.get(entity.getId());
        if (cached != null && cached.expiresAt() >= clientTick) {
            return cached.occluded();
        }

        if (raycastBudget.remaining() < SAMPLE_COUNT) {
            return false;
        }

        Vec3 camera = new Vec3(cameraX, cameraY, cameraZ);
        double width = bounds.maxX - bounds.minX;
        double height = bounds.maxY - bounds.minY;

        Vec3 center = new Vec3(centerX, centerY, centerZ);
        Vec3 upper = new Vec3(centerX, bounds.minY + height * 0.82D, centerZ);
        Vec3 side = new Vec3(bounds.minX + width * 0.82D, centerY, centerZ);

        boolean occluded = isOccluded(client, camera, center)
            && isOccluded(client, camera, upper)
            && isOccluded(client, camera, side);

        visibilityCache.put(
            entity.getId(),
            new CachedVisibility(occluded, clientTick + NovaBoostConfig.cacheTicks())
        );

        if (visibilityCache.size() > CACHE_CAPACITY) {
            visibilityCache.clear();
        }

        return occluded;
    }

    private boolean isOccluded(Minecraft client, Vec3 camera, Vec3 target) {
        if (!raycastBudget.tryAcquire()) {
            return false;
        }

        BlockHitResult hit = client.level.clip(new ClipContext(
            camera,
            target,
            ClipContext.Block.COLLIDER,
            ClipContext.Fluid.NONE,
            client.player
        ));

        if (hit.getType() != HitResult.Type.BLOCK) {
            return false;
        }

        BlockPos hitPosition = hit.getBlockPos();
        BlockState hitState = client.level.getBlockState(hitPosition);
        if (!hitState.canOcclude()) {
            return false;
        }

        double hitDistanceSquared = hit.getLocation().distanceToSqr(camera);
        double targetDistanceSquared = target.distanceToSqr(camera);
        return hitDistanceSquared + 0.04D < targetDistanceSquared;
    }

    private record CachedVisibility(boolean occluded, long expiresAt) {
    }
}
