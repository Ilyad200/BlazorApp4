window.compareScroll = (function () {

    let left = null;
    let right = null;

    let leftOffset = 0;
    let rightOffset = 0;

    let enabled = false;

    let syncing = false;

    const TOP_OFFSET = 20; 

    // Вспомогательная функция: получить текущее "верхнее" сообщение в элементе
    function getCurrentMessage(element) {
        const messages = element.querySelectorAll('.msg');
        if (messages.length === 0) return null;

        const containerRect = element.getBoundingClientRect();
        // Ищем первое сообщение, чей нижний край пересекает верхнюю границу контейнера
        for (let i = 0; i < messages.length; i++) {
            const rect = messages[i].getBoundingClientRect();
            if (rect.bottom > containerRect.top + TOP_OFFSET) {
                return messages[i];
            }
        }
        // Если ничего не найдено (все сообщения выше), возвращаем последнее
        return messages[messages.length - 1];
    }

    // Основная функция синхронизации
    function syncScroll(sourceElement, isCompare) {
        if (!enabled || syncing) return;

        const targetElement = isCompare ? left : right;
        if (!targetElement) return;

        const sourceMessages = sourceElement.querySelectorAll('.msg');
        if (sourceMessages.length === 0) return;

        // 1. Определяем текущее верхнее сообщение в источнике
        const sourceMsg = getCurrentMessage(sourceElement);
        if (!sourceMsg) return;

        const sourceOrder = parseInt(sourceMsg.dataset.order, 10);
        if (isNaN(sourceOrder)) return;

        // 2. Вычисляем целевой порядковый номер с учётом смещений
        let targetOrder;
        if (isCompare) {
            // Скроллим правый → левый
            targetOrder = sourceOrder - rightOffset + leftOffset;
        } else {
            // Скроллим левый → правый
            targetOrder = sourceOrder - leftOffset + rightOffset;
        }

        // 3. Находим целевое сообщение по data-order
        const targetMsg = targetElement.querySelector(`[data-order="${targetOrder}"]`);
        if (!targetMsg) {
            // Если такого сообщения нет – ничего не делаем
            return;
        }

        // 4. Вычисляем долю прокрутки внутри исходного сообщения
        const sourceRect = sourceMsg.getBoundingClientRect();
        const containerRect = sourceElement.getBoundingClientRect();
        // Позиция верхнего края сообщения относительно контейнера
        const sourceTopRelative = sourceRect.top - containerRect.top;
        // Текущая прокрутка контейнера
        const scrollTop = sourceElement.scrollTop;
        // Доля внутри сообщения (от 0 до 1)
        //const inside = (scrollTop - sourceMsg.offsetTop) / sourceMsg.offsetHeight;
        const inside = (containerRect.top - sourceRect.top) / sourceMsg.offsetHeight;
        // Ограничиваем, чтобы не выйти за пределы
        const clampedInside = Math.max(0, Math.min(1, inside));

        // 5. Вычисляем целевой scrollTop
        const targetRect = targetMsg.getBoundingClientRect();
        const targetContainerRect = targetElement.getBoundingClientRect();
        const targetTopRelative = targetRect.top - targetContainerRect.top;
        const pos = targetElement.scrollTop + (targetRect.top - targetContainerRect.top);
        let targetScroll = pos + clampedInside * targetMsg.getBoundingClientRect().height - TOP_OFFSET;

        // 6. Применяем прокрутку с блокировкой синхронизации
        syncing = true;
        targetElement.scrollTo({
            top: Math.max(0, targetScroll),
            behavior: 'auto'
        });

        // Сбрасываем флаг после того, как событие scroll обработается
        requestAnimationFrame(() => {
            syncing = false;
        });
    }

    // Обработчики скролла
    function onLeftScroll() {
        if (left) syncScroll(left, false);
    }

    function onRightScroll() {
        if (right) syncScroll(right, true);
    }

    return {

        // isLeft – true для левого, false для правого
        register(isLeft, element) {
            if (isLeft) {
                left = element;
            } else {
                right = element;
            }

            // Переподвешиваем слушатели
            if (left) {
                left.removeEventListener('scroll', onLeftScroll);
                left.addEventListener('scroll', onLeftScroll);
            }
            if (right) {
                right.removeEventListener('scroll', onRightScroll);
                right.addEventListener('scroll', onRightScroll);
            }
        },

        unregister(isCompare) {
            if (isCompare) {
                if (right) {
                    right.removeEventListener('scroll', onRightScroll);
                    right = null;
                }
            } else {
                if (left) {
                    left.removeEventListener('scroll', onLeftScroll);
                    left = null;
                }
            }
        },

        // Установка смещения для конкретной стороны
        setOffset(offset, isCompare) {
            if (offset === undefined || offset < 0) return;
            if (isCompare) {
                rightOffset = offset;
            } else {
                leftOffset = offset;
            }
        },

        enable(value) {
            enabled = value;
            // Если отключаем – сбрасываем флаг синхронизации
            if (!value) syncing = false;
        },
        scrollToMessage(order, isCompare) {
            // Ищем элемент сообщения по атрибуту data-order, который задается в ChatView.razor
            let msg = null;
            if (isCompare)
                msg = right.querySelector(`.msg[data-order="${order}"]`);
            else 
                msg = left.querySelector(`.msg[data-order="${order}"]`);
            if (msg) {
                // Плавно прокручиваем так, чтобы сообщение оказалось по центру экрана
                msg.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        }
    };

})();