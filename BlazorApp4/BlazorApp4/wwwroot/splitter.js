window.splitter = {
    instances: {},

    create(id, left, right, options) {
        const split = Split(
            [left, right],
            {
                sizes: options.sizes,
                minSize: options.minSize,
                gutterSize: options.gutterSize,
                direction: options.direction,

                onDragEnd: function (sizes) {
                    if (options.onChanged) {
                        options.onChanged.invokeMethodAsync(
                            "OnSizesChanged",
                            sizes
                        );
                    }
                }
            });

        this.instances[id] = split;
    },

    getSizes(id) {
        return this.instances[id]?.getSizes();
    },

    setSizes(id, sizes) {
        this.instances[id]?.setSizes(sizes);
    },

    destroy(id) {
        this.instances[id]?.destroy();
        delete this.instances[id];
    }
};