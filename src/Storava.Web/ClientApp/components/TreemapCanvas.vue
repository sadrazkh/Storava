<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { ScanItem } from '@/models/scan';

const props = defineProps<{ items: ScanItem[] }>();
const emit = defineEmits<{ select: [item: ScanItem] }>();
const canvas = ref<HTMLCanvasElement | null>(null);
let observer: ResizeObserver | null = null;
let hitAreas: Array<{ item: ScanItem; x: number; y: number; width: number; height: number }> = [];

const palette = ['#baf36b', '#68d5cb', '#f2b66d', '#88a7ff', '#e98289', '#a6d78d', '#d5c1ff'];

function draw(): void {
  const element = canvas.value;
  if (!element) return;
  const bounds = element.getBoundingClientRect();
  const ratio = Math.min(devicePixelRatio || 1, 2);
  element.width = Math.max(1, Math.round(bounds.width * ratio));
  element.height = Math.max(1, Math.round(bounds.height * ratio));
  const context = element.getContext('2d');
  if (!context) return;
  context.scale(ratio, ratio);
  context.clearRect(0, 0, bounds.width, bounds.height);
  const items = props.items.filter((item) => item.size > 0).slice(0, 40);
  const total = items.reduce((sum, item) => sum + item.size, 0);
  hitAreas = [];
  if (total === 0) return;

  let cursor = 0;
  items.forEach((item, index) => {
    const remaining = bounds.width - cursor;
    const width = index === items.length - 1
      ? remaining
      : Math.max(6, bounds.width * item.size / total);
    const x = cursor;
    const gap = 3;
    context.fillStyle = palette[index % palette.length] ?? '#68d5cb';
    context.globalAlpha = 0.78 + (index % 3) * 0.07;
    context.beginPath();
    context.roundRect(x + gap, gap, Math.max(1, width - gap * 2), bounds.height - gap * 2, 8);
    context.fill();
    if (width > 92) {
      context.globalAlpha = 1;
      context.fillStyle = '#071a1c';
      context.font = '600 12px Manrope, sans-serif';
      context.fillText(item.name.slice(0, 18), x + 12, 25, width - 20);
    }
    hitAreas.push({ item, x, y: 0, width, height: bounds.height });
    cursor += width;
  });
  context.globalAlpha = 1;
}

function selectAt(event: MouseEvent): void {
  const element = canvas.value;
  if (!element) return;
  const bounds = element.getBoundingClientRect();
  const x = event.clientX - bounds.left;
  const y = event.clientY - bounds.top;
  const hit = hitAreas.find((area) => x >= area.x && x <= area.x + area.width && y >= area.y && y <= area.height);
  if (hit) emit('select', hit.item);
}

watch(() => props.items, draw, { deep: true });
onMounted(() => {
  observer = new ResizeObserver(draw);
  if (canvas.value) observer.observe(canvas.value);
  draw();
});
onBeforeUnmount(() => observer?.disconnect());
</script>

<template>
  <canvas ref="canvas" class="treemap-canvas" tabindex="0" @click="selectAt" />
</template>
