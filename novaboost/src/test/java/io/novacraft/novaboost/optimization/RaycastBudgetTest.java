package io.novacraft.novaboost.optimization;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

final class RaycastBudgetTest {
    @Test
    void neverGrantsMoreQueriesThanConfigured() {
        RaycastBudget budget = new RaycastBudget(3);

        assertTrue(budget.tryAcquire());
        assertTrue(budget.tryAcquire());
        assertTrue(budget.tryAcquire());
        assertFalse(budget.tryAcquire());
        assertEquals(0, budget.remaining());
    }

    @Test
    void resetRestoresTheWholeBudget() {
        RaycastBudget budget = new RaycastBudget(2);
        assertTrue(budget.tryAcquire());
        assertTrue(budget.tryAcquire());

        budget.reset();

        assertEquals(2, budget.remaining());
        assertTrue(budget.tryAcquire());
    }

    @Test
    void configuredResetChangesTheLimitWithoutLeavingOldUsage() {
        RaycastBudget budget = new RaycastBudget(2);
        assertTrue(budget.tryAcquire());

        budget.reset(5);

        assertEquals(5, budget.remaining());
        assertTrue(budget.tryAcquire());
    }

    @Test
    void rejectsInvalidLimits() {
        assertThrows(IllegalArgumentException.class, () -> new RaycastBudget(0));
        assertThrows(IllegalArgumentException.class, () -> new RaycastBudget(-1));
    }
}
