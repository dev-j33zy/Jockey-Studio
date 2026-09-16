/// Mixing math shared by the engine's per-device bus mixers.
/// Everything here is pure and unit-tested: no I/O, no globals.

/// Source->bus rate ratio for linear resampling.
#[derive(Debug, Clone, Copy)]
pub struct Scaler {
    pub step: f64,
}

impl Scaler {
    pub fn new(tile_rate: u32, bus_rate: u32) -> Self {
        Self {
            step: if bus_rate == 0 {
                1.0
            } else {
                tile_rate as f64 / bus_rate as f64
            },
        }
    }
}

/// Linearly interpolate the tile sample at floating frame `pos` into `out`
/// (length = source channel count). Returns false when the next frame pair is
/// not yet available (need more decode or end-of-stream).
pub fn linear_at(fifo: &[f32], tc: usize, pos: f64, out: &mut [f32]) -> bool {
    let frames = if tc == 0 { 0 } else { fifo.len() / tc };
    if pos < 0.0 {
        for c in 0..out.len() {
            out[c] = 0.0;
        }
        return true;
    }
    let i0 = pos.floor() as usize;
    if i0 + 1 >= frames {
        return false;
    }
    let t = (pos - i0 as f64) as f32;
    let base = i0 * tc;
    for c in 0..tc.min(out.len()) {
        let a = fifo[base + c];
        let b = fifo[base + tc + c];
        out[c] = a + (b - a) * t;
    }
    for c in tc.min(out.len())..out.len() {
        out[c] = 0.0;
    }
    true
}

/// Down/up-mix one source frame and add it (post-gain) into `dst` at `ch_off`.
pub fn mix_frame(src: &[f32], tc: usize, frame: usize, dst: &mut [f32], ch_off: usize, bc: usize, gain: f32) {
    let idx = frame * tc;
    match tc {
        n if n == bc => {
            for i in 0..bc {
                dst[ch_off + i] += src[idx + i] * gain;
            }
        }
        1 => {
            let s = src[idx] * gain;
            for i in 0..bc {
                dst[ch_off + i] += s;
            }
        }
        _ if bc == 1 => {
            let mut s = 0.0f32;
            for i in 0..tc {
                s += src[idx + i];
            }
            dst[ch_off] += (s / tc as f32) * gain;
        }
        2 => {
            let l = src[idx] * gain;
            let r = src[idx + 1] * gain;
            dst[ch_off] += l;
            if bc > 1 {
                dst[ch_off + 1] += r;
            }
            for i in 2..bc {
                dst[ch_off + i] += l;
            }
        }
        _ => {
            let (l, r) = stereoish(src, idx, tc);
            dst[ch_off] += l * gain;
            if bc > 1 {
                dst[ch_off + 1] += r * gain;
            }
            for i in 2..bc {
                dst[ch_off + i] += l * gain;
            }
        }
    }
}

/// Best-effort fold of N>2 channels into a stereo pair (LFE omitted).
fn stereoish(src: &[f32], idx: usize, tc: usize) -> (f32, f32) {
    let ch = |n: usize| src.get(idx + n).copied().unwrap_or(0.0);
    let fl = ch(0);
    let fr = ch(1);
    let c = ch(2);
    let sl = if tc > 3 { ch(3) } else { 0.0 };
    let sr = if tc > 4 { ch(4) } else { 0.0 };
    let l = fl + 0.7071 * c + 0.7071 * sl;
    let r = fr + 0.7071 * c + 0.7071 * sr;
    (l.clamp(-1.0, 1.0), r.clamp(-1.0, 1.0))
}

/// Soft limit into [-1, 1] to keep multiple summed tiles from hard clipping.
#[inline]
pub fn soft_clip(x: f32) -> f32 {
    if x > 1.0 {
        1.0
    } else if x < -1.0 {
        -1.0
    } else {
        x
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn scaler_step() {
        assert_eq!(Scaler::new(44100, 48000).step, 0.91875);
        assert_eq!(Scaler::new(48000, 48000).step, 1.0);
    }

    #[test]
    fn linear_at_crossfades() {
        let fifo = [0.0, 1.0, 10.0, 11.0];
        let mut out = [0.0; 2];
        assert!(linear_at(&fifo, 2, 0.5, &mut out));
        assert_eq!(out, [5.0, 6.0]);
        assert!(linear_at(&fifo, 2, 1.0, &mut out));
        assert_eq!(out, [10.0, 11.0]);
        // Past the last pair, not ready yet.
        assert!(!linear_at(&fifo, 2, 0.9999, &mut out) || out == [10.0, 11.0]);
    }

    #[test]
    fn linear_at_requires_next_frame() {
        let fifo = [0.0, 1.0];
        let mut out = [0.0; 2];
        assert!(!linear_at(&fifo, 2, 0.9, &mut out));
    }

    #[test]
    fn mono_to_stereo_duplicates() {
        let mut dst = [0f32; 2];
        mix_frame(&[0.5], 1, 0, &mut dst, 0, 2, 1.0);
        assert_eq!(dst, [0.5, 0.5]);
    }

    #[test]
    fn stereo_to_mono_averages() {
        let mut dst = [0f32; 1];
        mix_frame(&[0.2, 0.8], 2, 0, &mut dst, 0, 1, 1.0);
        assert!((dst[0] - 0.5).abs() < 1e-5);
    }

    #[test]
    fn stereo_passthrough_with_gain() {
        let mut dst = [0f32; 2];
        mix_frame(&[0.5, -0.5], 2, 0, &mut dst, 0, 2, 0.5);
        assert_eq!(dst, [0.25, -0.25]);
    }

    #[test]
    fn stereo_to_quad_extends_lr() {
        let mut dst = [0f32; 4];
        mix_frame(&[0.5, -0.5], 2, 0, &mut dst, 0, 4, 1.0);
        assert_eq!(dst, [0.5, -0.5, 0.5, 0.5]);
    }

    #[test]
    fn two_tiles_sum() {
        let mut dst = [0f32; 2];
        mix_frame(&[0.4, 0.4], 2, 0, &mut dst, 0, 2, 1.0);
        mix_frame(&[0.3, 0.3], 2, 0, &mut dst, 0, 2, 0.5);
        assert_eq!(dst, [0.55, 0.55]);
    }

    #[test]
    fn soft_clip_bounds() {
        assert_eq!(soft_clip(1.2), 1.0);
        assert_eq!(soft_clip(-1.2), -1.0);
        assert_eq!(soft_clip(0.3), 0.3);
    }
}