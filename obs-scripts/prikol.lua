obs = obslua

------------------------------------------------------------
-- общий каркас шейдера
------------------------------------------------------------
local head = [[
uniform float4x4 ViewProj;
uniform texture2d image;
uniform float u_time;
uniform float res_x;
uniform float res_y;
]]

local body = [[
sampler_state textureSampler {
    Filter   = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VertData {
    float4 pos : POSITION;
    float2 uv  : TEXCOORD0;
};

VertData VSDefault(VertData v_in)
{
    VertData vert_out;
    vert_out.pos = mul(float4(v_in.pos.xyz, 1.0), ViewProj);
    vert_out.uv  = v_in.uv;
    return vert_out;
}

float rnd(float n)
{
    return frac(sin(n * 127.1) * 43758.5453);
}

float rnd2(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

]]

local tech = [[

technique Draw
{
    pass
    {
        vertex_shader = VSDefault(v_in);
        pixel_shader  = PSMain(v_in);
    }
}
]]

------------------------------------------------------------
-- регистрация фильтра по описанию
-- params: {имя, подпись, мин, макс, шаг, по умолчанию}
-- в шейдере параметр доступен как p_имя
------------------------------------------------------------
local function register_filter(def)
  local shader = head
  for _, p in ipairs(def.params) do
    shader = shader .. "uniform float p_" .. p[1] .. ";\n"
  end
  shader = shader .. body .. def.code .. tech

  local info = {}
  info.id = def.id
  info.type = obs.OBS_SOURCE_TYPE_FILTER
  info.output_flags = obs.OBS_SOURCE_VIDEO

  info.get_name = function()
    return def.name
  end

  info.create = function(settings, source)
    local data = {}
    data.source = source
    data.time = 0
    data.width = 1
    data.height = 1
    data.values = {}
    data.p = {}

    obs.obs_enter_graphics()
    data.effect = obs.gs_effect_create(shader, def.id, nil)
    obs.obs_leave_graphics()

    if data.effect == nil then
      obs.script_log(obs.LOG_ERROR, "Не удалось скомпилировать шейдер: " .. def.name)
      return nil
    end

    data.p_time = obs.gs_effect_get_param_by_name(data.effect, "u_time")
    data.p_resx = obs.gs_effect_get_param_by_name(data.effect, "res_x")
    data.p_resy = obs.gs_effect_get_param_by_name(data.effect, "res_y")
    for _, p in ipairs(def.params) do
      data.p[p[1]] = obs.gs_effect_get_param_by_name(data.effect, "p_" .. p[1])
    end

    info.update(data, settings)
    return data
  end

  info.destroy = function(data)
    if data.effect ~= nil then
      obs.obs_enter_graphics()
      obs.gs_effect_destroy(data.effect)
      obs.obs_leave_graphics()
    end
  end

  info.update = function(data, settings)
    for _, p in ipairs(def.params) do
      data.values[p[1]] = obs.obs_data_get_double(settings, p[1])
    end
  end

  info.get_defaults = function(settings)
    for _, p in ipairs(def.params) do
      obs.obs_data_set_default_double(settings, p[1], p[6])
    end
  end

  info.get_properties = function(data)
    local props = obs.obs_properties_create()
    for _, p in ipairs(def.params) do
      obs.obs_properties_add_float_slider(props, p[1], p[2], p[3], p[4], p[5])
    end
    return props
  end

  info.video_tick = function(data, seconds)
    data.time = data.time + seconds
    local target = obs.obs_filter_get_target(data.source)
    if target ~= nil then
      data.width  = obs.obs_source_get_base_width(target)
      data.height = obs.obs_source_get_base_height(target)
    end
  end

  info.get_width  = function(data) return data.width end
  info.get_height = function(data) return data.height end

  info.video_render = function(data, effect)
    if data.effect == nil or data.width == 0 or data.height == 0 then
      obs.obs_source_skip_video_filter(data.source)
      return
    end

    if not obs.obs_source_process_filter_begin(data.source, obs.GS_RGBA, obs.OBS_NO_DIRECT_RENDERING) then
      return
    end

    obs.gs_effect_set_float(data.p_time, data.time % 1000)
    obs.gs_effect_set_float(data.p_resx, data.width)
    obs.gs_effect_set_float(data.p_resy, data.height)
    for _, p in ipairs(def.params) do
      obs.gs_effect_set_float(data.p[p[1]], data.values[p[1]] or 0)
    end

    obs.obs_source_process_filter_end(data.source, data.effect, data.width, data.height)
  end

  obs.obs_register_source(info)
end

------------------------------------------------------------
-- 1. JPEG-зация
------------------------------------------------------------
register_filter({
  id = "glitch_jpeg",
  name = "Глюк: JPEG-зация",
  params = {
    {"block",    "Размер блока (px)",          4, 64, 1,    16},
    {"compress", "Сила сжатия",                0, 1, 0.01,  0.7},
    {"chroma",   "Потеря цвета в блоках",      0, 1, 0.01,  0.6},
    {"pulse",    "Дыхание качества",           0, 1, 0.01,  0},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float2 bsz = float2(p_block / res_x, p_block / res_y);
    float2 origin = floor(uv / bsz) * bsz;

    float3 avg = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < 4; i++) {
        for (int j = 0; j < 4; j++) {
            float2 sp = origin + bsz * float2((i + 0.5) / 4.0, (j + 0.5) / 4.0);
            avg += image.Sample(textureSampler, sp).rgb;
        }
    }
    avg = avg / 16.0;

    float4 src = image.Sample(textureSampler, uv);

    float comp = saturate(p_compress * (1.0 - p_pulse * (0.5 + 0.5 * sin(u_time * 2.0))));
    float levels = lerp(48.0, 1.5, comp);
    float cl = lerp(64.0, 3.0, comp);

    float3 aq = floor(avg * cl + 0.5) / cl;
    float3 detail = src.rgb - avg;
    float ld = dot(detail, float3(0.299, 0.587, 0.114));
    float3 cd = detail - float3(ld, ld, ld);
    ld = floor(ld * levels + 0.5) / levels;
    cd = cd * (1.0 - p_chroma);

    float3 outc = aq + float3(ld, ld, ld) + cd;
    return float4(saturate(outc), src.a);
}
]]
})

------------------------------------------------------------
-- 2. Плывущие пиксели
------------------------------------------------------------
register_filter({
  id = "glitch_floating_pixels",
  name = "Глюк: Плывущие пиксели",
  params = {
    {"size",  "Размер пикселя",        4, 64, 1,    12},
    {"amp",   "Амплитуда плавания",    0, 2, 0.01,  0.6},
    {"speed", "Скорость",              0, 10, 0.1,  2},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float2 cell = float2(p_size / res_x, p_size / res_y);
    float2 id = floor(uv / cell);
    float ph1 = rnd2(id) * 6.2831;
    float ph2 = rnd2(id + float2(7.0, 3.0)) * 6.2831;
    float2 center = (id + float2(0.5, 0.5)) * cell;
    float2 off = float2(sin(u_time * p_speed + ph1), cos(u_time * p_speed * 0.8 + ph2));
    off = off * p_amp * cell * 2.0;
    return image.Sample(textureSampler, center + off);
}
]]
})

------------------------------------------------------------
-- 3. Волна
------------------------------------------------------------
register_filter({
  id = "glitch_wave",
  name = "Глюк: Волна",
  params = {
    {"amp",   "Сила",              0, 0.1, 0.001, 0.02},
    {"freq",  "Частота",           1, 100, 0.5,   15},
    {"speed", "Скорость",          0, 20, 0.1,    3},
    {"angle", "Направление (°)",   0, 360, 1,     0},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float a = p_angle * 0.0174533;
    float2 dir = float2(cos(a), sin(a));
    float2 perp = float2(-dir.y, dir.x);
    float phase = dot(uv, dir) * p_freq - u_time * p_speed;
    uv += perp * sin(phase) * p_amp;
    uv += perp * sin(phase * 1.7 + 2.0) * p_amp * 0.4;
    uv += dir * sin(phase * 0.5 + 1.3) * p_amp * 0.3;
    return image.Sample(textureSampler, uv);
}
]]
})

------------------------------------------------------------
-- 4. Таяние
------------------------------------------------------------
register_filter({
  id = "glitch_melt",
  name = "Глюк: Таяние",
  params = {
    {"amount", "Длина потёков",       0, 1, 0.01,  0.5},
    {"cols",   "Кол-во колонок",      4, 300, 1,   80},
    {"speed",  "Скорость",            0, 5, 0.05,  0.8},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float col = floor(uv.x * p_cols);
    float n = rnd(col + 1.0);
    float n2 = rnd(col + 17.0);
    float w = 0.5 + 0.5 * sin(u_time * p_speed * (0.4 + n2) + n * 6.2831);
    float h = 1.0 - p_amount * w * (0.2 + 0.8 * n);
    uv.y = min(uv.y, h);
    return image.Sample(textureSampler, uv);
}
]]
})

------------------------------------------------------------
-- 5. Кристаллы (ячейки Вороного)
------------------------------------------------------------
register_filter({
  id = "glitch_crystals",
  name = "Глюк: Кристаллы",
  params = {
    {"cells",   "Кол-во ячеек",       4, 100, 1,    25},
    {"jitter",  "Подвижность",        0, 1, 0.01,   0.9},
    {"speed",   "Скорость",           0, 5, 0.05,   0.5},
    {"contour", "Контур ячеек",       0, 1, 0.01,   0.25},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float aspect = res_x / res_y;
    float2 asp = float2(aspect, 1.0);
    float2 p = uv * asp * p_cells;
    float2 ip = floor(p);
    float2 fp = frac(p);

    float d1 = 10.0;
    float d2 = 10.0;
    float2 best = ip + float2(0.5, 0.5);

    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            float2 g = float2(i, j);
            float2 id = ip + g;
            float2 rv = float2(rnd2(id), rnd2(id + float2(31.0, 17.0)));
            float2 pt = g + float2(0.5, 0.5) + 0.5 * p_jitter * sin(u_time * p_speed + rv * 6.2831);
            float dist = length(pt - fp);
            if (dist < d1) {
                d2 = d1;
                d1 = dist;
                best = ip + pt;
            } else if (dist < d2) {
                d2 = dist;
            }
        }
    }

    float2 suv = best / (asp * p_cells);
    float4 c = image.Sample(textureSampler, suv);
    float edge = smoothstep(0.0, 0.1, d2 - d1);
    c.rgb = c.rgb * lerp(1.0, edge, p_contour);
    return c;
}
]]
})

------------------------------------------------------------
-- 6. Закрутка
------------------------------------------------------------
register_filter({
  id = "glitch_swirl",
  name = "Глюк: Закрутка",
  params = {
    {"strength", "Сила закрутки",     -10, 10, 0.1,  4},
    {"radius",   "Радиус",            0.1, 1.5, 0.01, 0.6},
    {"speed",    "Скорость пульсации", 0, 5, 0.05,  1},
    {"pulse",    "Пульсация",         0, 1, 0.01,   0.7},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float aspect = res_x / res_y;
    float2 asp = float2(aspect, 1.0);
    float2 d = (uv - float2(0.5, 0.5)) * asp;
    float r = length(d);
    float k = saturate(1.0 - r / p_radius);
    float ang = p_strength * k * k * (1.0 - p_pulse + p_pulse * sin(u_time * p_speed));
    float s = sin(ang);
    float c = cos(ang);
    float2 rd = float2(d.x * c - d.y * s, d.x * s + d.y * c);
    float2 suv = rd / asp + float2(0.5, 0.5);
    return image.Sample(textureSampler, suv);
}
]]
})

------------------------------------------------------------
-- 7. Калейдоскоп
------------------------------------------------------------
register_filter({
  id = "glitch_kaleido",
  name = "Глюк: Калейдоскоп",
  params = {
    {"segments", "Кол-во сегментов",  2, 24, 1,     6},
    {"speed",    "Вращение",          -3, 3, 0.05,  0.3},
    {"zoom",     "Масштаб",           0.3, 3, 0.01, 1},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float aspect = res_x / res_y;
    float2 asp = float2(aspect, 1.0);
    float2 d = (uv - float2(0.5, 0.5)) * asp;
    float r = length(d);
    float a = atan2(d.y, d.x) + u_time * p_speed;
    float seg = 6.2831853 / p_segments;
    a = a - seg * floor(a / seg);
    a = abs(a - seg * 0.5);
    float2 p = float2(cos(a), sin(a)) * r * p_zoom;
    float2 suv = p / asp + float2(0.5, 0.5);
    suv = 1.0 - abs(frac(suv * 0.5) * 2.0 - 1.0);
    return image.Sample(textureSampler, suv);
}
]]
})

------------------------------------------------------------
-- 8. Битые макроблоки
------------------------------------------------------------
register_filter({
  id = "glitch_macroblocks",
  name = "Глюк: Битые макроблоки",
  params = {
    {"block",  "Размер блока (px)",       8, 128, 1,    32},
    {"chance", "Доля битых блоков",       0, 1, 0.01,   0.25},
    {"rate",   "Частота смены (раз/сек)", 0.2, 15, 0.1, 3},
    {"power",  "Дальность смещения",      0, 0.5, 0.005, 0.15},
    {"lost",   "Потерянные (плоские)",    0, 1, 0.01,   0.3},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float2 bsz = float2(p_block / res_x, p_block / res_y);
    float2 id = floor(uv / bsz);

    float t = floor(u_time * p_rate + rnd2(id) * 4.0);
    float h = rnd2(id + float2(t, t * 0.37));
    float2 off = (float2(rnd2(id + float2(t, 1.0)), rnd2(id + float2(t, 2.0))) - float2(0.5, 0.5)) * 2.0 * p_power;
    float isLost = step(rnd2(id + float2(t, 3.0)), p_lost);
    float bad = step(h, p_chance);

    float2 center = (id + float2(0.5, 0.5)) * bsz;
    float4 base = image.Sample(textureSampler, uv);
    float4 shifted = image.Sample(textureSampler, uv + off);
    float4 flat = image.Sample(textureSampler, center + off);

    float4 corrupted = lerp(shifted, flat, isLost);
    return lerp(base, corrupted, bad);
}
]]
})

------------------------------------------------------------
-- 9. Орбита каналов
------------------------------------------------------------
register_filter({
  id = "glitch_orbit",
  name = "Глюк: Орбита каналов",
  params = {
    {"radius", "Радиус орбиты",   0, 0.15, 0.001, 0.025},
    {"speed",  "Скорость",        0, 10, 0.1,     2},
  },
  code = [[
float4 PSMain(VertData v_in) : TARGET
{
    float2 uv = v_in.uv;
    float a = u_time * p_speed;
    float2 fix = float2(res_y / res_x, 1.0);
    float2 o1 = float2(cos(a), sin(a)) * p_radius * fix;
    float2 o2 = float2(cos(a + 2.094), sin(a + 2.094)) * p_radius * fix;
    float2 o3 = float2(cos(a + 4.189), sin(a + 4.189)) * p_radius * fix;
    float4 c = image.Sample(textureSampler, uv);
    c.r = image.Sample(textureSampler, uv + o1).r;
    c.g = image.Sample(textureSampler, uv + o2).g;
    c.b = image.Sample(textureSampler, uv + o3).b;
    return c;
}
]]
})