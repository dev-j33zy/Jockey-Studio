import { describe, expect, it } from "vitest";
import { formatDuration, formatTime } from "../lib/util";

describe("formatTime", () => {
  it("formats minutes and seconds", () => {
    expect(formatTime(0)).toBe("00:00");
    expect(formatTime(65)).toBe("01:05");
    expect(formatTime(3599)).toBe("59:59");
  });

  it("formats hours when present", () => {
    expect(formatTime(3661)).toBe("1:01:01");
  });

  it("handles negative and NaN", () => {
    expect(formatTime(-5)).toBe("00:00");
    expect(formatTime(Number.NaN)).toBe("00:00");
  });
});

describe("formatDuration", () => {
  it("returns placeholder for unknown durations", () => {
    expect(formatDuration(0)).toBe("--:--");
    expect(formatDuration(-1)).toBe("--:--");
  });

  it("formats known durations", () => {
    expect(formatDuration(125)).toBe("02:05");
  });
});