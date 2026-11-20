## Что положить в Assets

Кидать `.onnx` можно с любым именем — приложение само найдёт. Рекомендуемый экспорт
(из обычного venv с ultralytics):

```
pip install ultralytics
yolo export model=yolov8n.pt format=onnx imgsz=640
```

Получившийся `yolov8n.onnx` → в `Detector\Assets\`. YOLOv5n тоже поддержан
(`format=onnx` из yolov5 даёт `[1,25200,85]`). Если экспорт делать с `nms=True`
(ultralytics), придёт `[1,300,6]` — этот вариант декодер тоже понимает, NMS в этом
случае просто не с чем работать. Старый `TinyYolo2_model.onnx` больше не поддерживается
намеренно: в логе будет прямое сообщение об этом вместо мусора.



## Как применять

Вариант А: (git, клон репозитория):
```
cd Detector
git apply fix_Detector.patch
```
Вариант Б: распаковать `Detector_fixed.zip` поверх (или взять файлы из архива).

Тесты (по желанию, работают и на Windows): `cd tests\PostprocessTests && dotnet run`.


Проверки этой версии описаны в `INSTALL_SELECTION_FIX.md`.
