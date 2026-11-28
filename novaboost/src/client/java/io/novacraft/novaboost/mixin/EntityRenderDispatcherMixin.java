package io.novacraft.novaboost.mixin;

import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.client.renderer.entity.EntityRenderDispatcher;
import net.minecraft.world.entity.Entity;

import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

import io.novacraft.novaboost.optimization.EntityOcclusionCuller;

@Mixin(EntityRenderDispatcher.class)
public abstract class EntityRenderDispatcherMixin {
    @Inject(
        method = "shouldRender(Lnet/minecraft/world/entity/Entity;Lnet/minecraft/client/renderer/culling/Frustum;DDD)Z",
        at = @At("RETURN"),
        cancellable = true
    )
    private void novaboost$skipOccludedEntity(
        Entity entity,
        Frustum frustum,
        double cameraX,
        double cameraY,
        double cameraZ,
        CallbackInfoReturnable<Boolean> callback
    ) {
        // Let Minecraft complete its own visibility/frustum checks first.
        // NovaBoost only performs extra raycasts for entities vanilla would render.
        if (!Boolean.TRUE.equals(callback.getReturnValue())) {
            return;
        }

        Minecraft client = Minecraft.getInstance();
        if (EntityOcclusionCuller.INSTANCE.shouldCull(client, entity, cameraX, cameraY, cameraZ)) {
            callback.setReturnValue(false);
        }
    }
}
