obs = obslua

LIMIT = 10  * 3600         -- для теста 10 секунд; потом поставьте 10 * 3600
CHECK_INTERVAL = 2000  -- для теста 2 секунды; потом можно 10000
start_time = nil
restarting = false

function check()
  if start_time and obs.obs_frontend_recording_active() and not restarting then
    if os.time() - start_time >= LIMIT then
      print("Лимит достигнут, останавливаю запись")
      restarting = true
      obs.obs_frontend_recording_stop()
    end
  end
end

function delayed_start()
  obs.timer_remove(delayed_start)
  print("Запускаю запись заново")
  obs.obs_frontend_recording_start()
end

function on_event(event)
  if event == obs.OBS_FRONTEND_EVENT_RECORDING_STARTED then
    start_time = os.time()
    restarting = false
    print("Запись началась")
  elseif event == obs.OBS_FRONTEND_EVENT_RECORDING_STOPPED then
    start_time = nil
    print("Запись остановлена")
    if restarting then
      obs.timer_add(delayed_start, 2000)
    end
  end
end

function script_load(settings)
  obs.obs_frontend_add_event_callback(on_event)
  obs.timer_add(check, CHECK_INTERVAL)
  -- если запись уже идёт при загрузке скрипта, считаем отсчёт от этого момента
  if obs.obs_frontend_recording_active() then
    start_time = os.time()
    print("Запись уже шла, отсчёт начат с момента загрузки скрипта")
  end
end

function script_unload()
  obs.timer_remove(check)
end