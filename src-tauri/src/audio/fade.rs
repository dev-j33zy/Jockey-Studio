/// Minimal gain envelope used for fades and auto-mix. Pure and unit-testable.
///
/// A tile owns one [FadeEnvelope]. `fade_to(target, seconds)` starts a ramp;
/// advancing with `tween(dt)` moves `level` toward `target` at `1/seconds`
/// per second. A ramp with `seconds <= 0` snaps instantly.
#[derive(Debug, Clone, Copy)]
pub struct FadeEnvelope {
    pub level: f32,
    target: f32,
    rate: f32,
}

impl Default for FadeEnvelope {
    fn default() -> Self {
        Self {
            level: 0.0,
            target: 0.0,
            rate: 0.0,
        }
    }
}

impl FadeEnvelope {
    pub fn new() -> Self {
        Self::default()
    }

    /// Ramp `level` to `target` over `seconds`. `seconds <= 0` snaps instantly.
    pub fn fade_to(&mut self, target: f32, seconds: f32) {
        let target = target.clamp(0.0, 1.0);
        self.target = target;
        self.rate = if seconds <= 0.0 {
            0.0
        } else {
            1.0 / seconds
        };
    }

    pub fn instant_to(&mut self, v: f32) {
        let v = v.clamp(0.0, 1.0);
        self.level = v;
        self.target = v;
        self.rate = 0.0;
    }

    /// Advance the envelope by `dt` seconds. Returns true when `level` has
    /// reached `target`.
    pub fn tween(&mut self, dt: f32) -> bool {
        if self.rate == 0.0 {
            self.level = self.target;
            return true;
        }
        if self.level < self.target {
            self.level = (self.level + self.rate * dt).min(self.target);
        } else if self.level > self.target {
            self.level = (self.level - self.rate * dt).max(self.target);
        }
        (self.level - self.target).abs() < 1e-6
    }

    pub fn is_settled(&mut self) -> bool {
        self.tween(0.0)
    }

    pub fn is_audible(&self) -> bool {
        self.level > 0.0005
    }

    pub fn target(&self) -> f32 {
        self.target
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn snap_instant() {
        let mut f = FadeEnvelope::new();
        f.fade_to(1.0, 0.0);
        assert!(f.tween(0.1));
        assert_eq!(f.level, 1.0);
    }

    #[test]
    fn ramps_and_clamps() {
        let mut f = FadeEnvelope::new();
        f.fade_to(1.0, 1.0);
        assert!(!f.tween(0.25));
        assert!((f.level - 0.25).abs() < 1e-4);
        assert!(f.tween(10.0));
        assert_eq!(f.level, 1.0);
    }

    #[test]
    fn fade_out_reaches_zero() {
        let mut f = FadeEnvelope::new();
        f.instant_to(1.0);
        f.fade_to(0.0, 0.5);
        assert!(!f.tween(0.2));
        assert!(f.level > 0.0);
        assert!(f.tween(1.0));
        assert_eq!(f.level, 0.0);
        assert!(!f.is_audible());
    }

    #[test]
    fn target_clamped_to_unit() {
        let mut f = FadeEnvelope::new();
        f.fade_to(2.0, 0.0);
        assert_eq!(f.target(), 1.0);
        f.fade_to(-1.0, 0.0);
        assert_eq!(f.target(), 0.0);
    }
}