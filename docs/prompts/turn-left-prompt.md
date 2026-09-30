# 转身动作 Prompt：正面 → 面向左侧（turn_left）

面向桌宠 `walk_left` 素材的转身镜头提示词。参考图为 3D Q 版女孩正面站姿（纯绿色背景）：
黑色中分长发、柔和肤色、温柔微笑、灰色长大衣、白色翻领衬衫、蓝色宽松牛仔裤、黑白鞋。

---

## 一、主提示词（可直接复制）

```
Use the attached image as the exact character reference.

Create an 8-second video of the character turning from facing the camera
to facing the left side of the frame, then turning back to face the camera.
This small turn is the only motion in the whole video. Nothing else moves.

Preserve the same cute 3D chibi girl:
long black center-parted hair, soft fair skin, gentle small smile,
gray long coat, white collared shirt, blue wide-leg jeans,
black-and-white sneakers, short childlike proportions.

Keep the character identity, face, hairstyle, clothing, colors,
body proportions, lighting, size, and position completely consistent.

Full-body character centered in frame.
Keep the entire hair, hands, coat, legs, and shoes visible.
Keep both feet fixed on the same horizontal baseline.

LOCK THE CAMERA FOR THE ENTIRE 8 SECONDS.
Use the exact same front-facing camera view as the reference image.
The camera position, camera angle, perspective, focal length,
framing, and distance to the character must never change.
No zoom in, no zoom out, no push-in, no pull-back,
no pan, no tilt, no orbit, no camera shake, no reframing.
The character must stay the exact same height in every frame.

ONLY THE CHARACTER ROTATES, and only around her own vertical axis (yaw).
The rotation pivot is a fixed vertical line through the center of her body.
Her feet stay planted on the same spot on the same baseline.
She does not walk, does not step forward, backward, left, or right,
and does not slide or drift across the ground.
No horizontal or vertical travel of the whole body at any time.

Timeline:
0.00-1.50 seconds:
Stand still facing the camera in the exact reference pose, eyes open.

1.50-4.50 seconds:
Rotate smoothly around the vertical axis toward the LEFT side of the frame
until she faces the left side, about a 90-degree turn.
Her nose and chest swing toward frame-left. We end up seeing her right side.
The turn is a clean pivot in place, with no stepping and no body travel.

4.50-5.50 seconds:
Hold the left-facing pose, still and stable.
Do not drift, do not keep rotating past 90 degrees.

5.50-7.00 seconds:
Rotate smoothly back the other way, from left-facing to front-facing.

7.00-8.00 seconds:
Stand still facing the camera in the exact original reference pose.

During the whole turn, her hair and her coat rotate together with her
as one solid body: the hair keeps the same shape, length, and volume,
and the coat keeps the same silhouette, buttons, and folds.
No secondary motion, no hair swing, no cloth flutter, no trailing.

Keep her arms hanging relaxed at her sides in the same pose.
Keep her hands, legs, and feet in the same relaxed standing pose.
Do not open her mouth, do not change her gentle smile,
do not move her eyebrows, do not change her gaze.
Eyelids stay open the whole time.

Every frame must be sharp and fully in focus.
No motion blur, no smear, no ghosting on any frame.

Use a perfectly flat solid chroma green background,
uniform green color, no texture, no gradient, no shadow,
no floor, no scenery, no props, no text.
Keep the background color and lighting identical in every frame.

The first and final frames must match exactly: same front-facing pose,
same position, same scale, same framing, same expression, same silhouette.
The video loops seamlessly without any visible jump.

The animation must be clean, readable, and suitable for extracting
individual PNG frames for a desktop pet sprite animation.
```

## 二、负面提示词（可直接复制）

```
camera movement, camera shake, zoom in, zoom out, push-in, pull-back,
pan, tilt, orbit, camera rotation, perspective change, focal length change,
reframing, changing character size, changing character height,
character walking, stepping, footstep, leg lift, sliding feet,
sliding across ground, body translation, floating character,
jumping, leaning, tilting body, bending over,
rotating more than 90 degrees, over-rotating, spinning, turning around
to show the back, showing the back of the head, back view only,
overshoot, elastic bounce, wobble,
identity change, different face, different hairstyle, different clothes,
color shift, body deformation, extra fingers, missing fingers,
extra limbs, duplicated body parts, melted hands, distorted shoes,
cropped hair, cropped feet, cut-off body, cropped head,
hair movement, hair swing, hair flying, cloth flutter, coat blowing,
breathing motion, chest movement, shoulder movement, body bob, body sway,
head movement, head turn before the body, mouth movement, talking,
eyebrow movement, expression change, wink, blinking, eye rolling,
gaze shift, closed eyes,
motion blur, smear, ghosting, strobing, flicker, double contour,
background movement, green-screen color variation, green halo,
shadows, floor shadow, lighting flicker,
particles, sparkles, text, logo, watermark, scenery, props,
additional characters, second person
```

---

## 三、运动路径（时间轴 / 帧表）

总时长 8 秒，唯一运动是角色绕自身垂直轴的原地偏航旋转。无镜头运动，无位移。

| 时间段 | 动作 | 说明 |
| --- | --- | --- |
| 0.00–1.50 s | 正面静止 | 与参考图完全一致的正面站姿，双眼睁开，微笑不变 |
| 1.50–4.50 s | 向左转 90° | 鼻尖与胸口转向画面左侧，缓入缓出，原地轴转，不迈步、不位移 |
| 4.50–5.50 s | 保持左侧朝向 | 稳定停住，不得继续转过头或回弹 |
| 5.50–7.00 s | 转回正面 | 反向平滑转回 |
| 7.00–8.00 s | 正面静止 | 与首帧完全一致，可直接循环 |

**朝向判读（关键，容易弄反）**
角色面朝画面左侧站定后，我们看到的是**她的右侧**：右侧脸颊、右肩、右臂、右腿在前，鼻子指向画面左边缘。转身过程中画面宽度会自然地由正面的全宽收窄到侧面的窄轮廓，这是正确现象——但她的**画面高度必须保持不变**，且**双脚脚底始终在同一水平基线上、原地不滑动**。

**帧率与时长建议**
- 输出 8 s / 30 fps 或更高（60 fps 更好，便于后续重采样）。
- 若沿用现有素材的 8 fps 采样，可只抽取 4.50–5.50 s 的保持段作为左向静止姿势帧，抽取 0.00–4.50 s 作为正面到左向的转身过渡帧；8 s 整段按 30 fps 抽取则得到 240 帧。

---

## 四、过渡方案

- 转身用**整段连续偏航旋转**完成，不做任何跳切、淡入淡出、交叉溶解或黑场过渡。
- 旋转采用平滑缓入缓出（ease-in-out），起步和收尾都要减速，中段匀速，全程不出现突然加速或抽帧跳变。
- 禁止用整体图片缩放、镜像翻转、位移、贴图替换或面部变形来伪造转身。特别注意：**不要用左右镜像翻转冒充左向朝向**——镜像会翻转她中分头发和衣襟的细节，与参考图不一致。
- 到达左侧朝向时不得过冲（no overshoot）、不得弹性回弹、不得左右摇摆。
- 头发与大衣作为身体的一部分整体旋转，不追加跟随摆动或拖尾。
- 首尾均为同一个正面静止状态，切换回正面（idle）时可无跳变衔接。

---

## 五、交付规格与首尾帧检查

**交付要求**
- 视频 8 s，30 fps 或以上，无音轨。
- 分辨率不低于 1080×1080，优先 1:1 或接近参考图比例；角色整体不被裁切，头发、双手、大衣下摆、鞋底全部在画面内。
- 首帧必须与参考图一致，便于直接作为第 0 帧对齐。

**首尾帧检查**
1. 首帧与尾帧逐项比对：正面朝向、双眼睁开、温柔微笑、双手自然垂下、双脚位于同一水平基线，两者必须完全相同。
2. 核对头顶、肩膀、双手、大衣下摆和鞋底位置：转身过程中**不得发生上下位移**，画面高度不得变化。
3. 双脚脚底基线全程不变，不得出现滑动、抬起或原地迈步。
4. 镜头视角、透视、焦距、构图和角色在画面中的占比全程一致；不得放大、缩小、推近、拉远、转动或重新构图。
5. 绿幕背景全程均匀纯绿，无光照变化、无阴影、无闪烁、无绿色溢色光晕。
6. 逐帧检查无运动模糊与残影；转身结束时姿态干净利落，无重影轮廓。
7. 循环衔接处无位置跳动、无大小变化、无表情突变。

**落地到项目素材（供后续使用）**
- 透明 PNG 序列，画布固定 **180 × 210**，脚底基线一致，底部留 2 px，水平居中。
- 命名 `turn_left_000.png` 起、连续无缺号，放入 `src/DesktopPet/Assets/Character/`，重新构建即被资源打包。
- 注意：`SpriteAnimator` 的动画表当前为 `idle` / `walk_left` / `walk_right` / `respond` / `sleep` / `drag`，尚无 `turn_left` 前缀，也尚无 Turn 状态——本轮只产出素材，接入需要另行扩展该表与状态机。

---

## 六、风险与备选

- **3/4 视角下的面部崩坏**是这类转身镜头最常见的失败点（眼睛、鼻子、发际线在斜侧角度变形）。若一次生成不达标，建议拆成两条分别生成：一条只做正面→左向的 90° 转身，另一条只做左向静止姿势，分别验收，避免把手部错误和朝向错误混在同一条里排查。
- 若模型总是把转身做成"走一步再转"，把提示词中的 `pivot in place`、`feet stay planted`、`no stepping` 提到更靠前的位置，并在负面词中加强 `stepping, leg lift, sliding feet`。
- 若转身过程中出现整体漂移（画面里角色位置轻微平移），抽帧后在合入 180×210 画布时，用脚底基线与水平中心做统一锚点对齐即可抵消。
