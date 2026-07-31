import assert from "node:assert/strict";
import test from "node:test";
import { createLongMock } from "../mock/index.js";
import {
  createReferenceWidget
} from "../../../samples/LongWidgetReference/widgets/shared/widget-controller.js";

function widgetMock(instanceId, widgetState = null) {
  return createLongMock({
    pluginId: "com.long.reference-widgets",
    surface: "widget",
    widgetId: "tiny-counter",
    instanceId,
    widgetState,
    features: ["widget.instance-state", "widget.lifecycle.v1"]
  });
}

test("reference widget negotiates identity and restores bounded instance state", async () => {
  const mock = widgetMock("instance-a", { value: 7, mode: "detail" });

  const controller = await createReferenceWidget(mock.long);

  assert.equal(controller.snapshot().info.instance_id, "instance-a");
  assert.deepEqual(controller.snapshot().state, {
    value: 7,
    mode: "detail"
  });
  assert.equal(mock.getCalls("host.getInfo").length, 1);
  assert.equal(mock.getCalls("widget.ready").length, 1);
});

test("reference widget persists mutations and keeps multiple instances isolated", async () => {
  const first = widgetMock("instance-a");
  const second = widgetMock("instance-b", { value: 12, mode: "calm" });
  const firstController = await createReferenceWidget(first.long);
  const secondController = await createReferenceWidget(second.long);

  await firstController.increment();
  await firstController.toggleMode();
  await firstController.requestRefresh("test-refresh");

  assert.deepEqual(firstController.snapshot().state, {
    value: 1,
    mode: "detail"
  });
  assert.deepEqual(secondController.snapshot().state, {
    value: 12,
    mode: "calm"
  });
  assert.equal(first.getCalls("widget.setInstanceState").length, 2);
  assert.equal(first.getCalls("widget.setBadge").length, 2);
  assert.deepEqual(first.getCalls("widget.invalidate")[0].args, [
    "test-refresh"
  ]);
  assert.equal(second.getCalls("widget.setInstanceState").length, 0);
});

test("reference widget fails closed outside a trusted widget surface", async () => {
  const mock = createLongMock({
    pluginId: "com.long.reference-widgets",
    surface: "plugin"
  });

  await assert.rejects(
    () => createReferenceWidget(mock.long),
    /trusted widget context/);
  assert.equal(mock.getCalls("widget.ready").length, 0);
});
