package io.novacraft.novaboost.optimization;

public final class RaycastBudget {
    private int limit;
    private int used;

    public RaycastBudget(int limit) {
        setLimit(limit);
    }

    public boolean tryAcquire() {
        if (used >= limit) {
            return false;
        }
        used++;
        return true;
    }

    public void reset() {
        used = 0;
    }

    public void reset(int newLimit) {
        setLimit(newLimit);
        used = 0;
    }

    public int remaining() {
        return limit - used;
    }

    private void setLimit(int value) {
        if (value < 1) {
            throw new IllegalArgumentException("Raycast budget must be positive.");
        }
        limit = value;
    }
}
