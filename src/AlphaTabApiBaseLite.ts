import { IContainer } from '@src/platform/IContainer';
import { Score } from '@src/model/Score';
import { Track } from '@src/model/Track';
import { IUiFacade } from '@src/platform/IUiFacade';
import { Settings } from '@src/Settings';
import { IScoreRenderer } from '@src/rendering/IScoreRenderer';
import { Logger } from '@src/Logger';
import { Environment } from '@src/Environment';
import { ModelUtils } from '@src/model/ModelUtils';
import { EventEmitter, IEventEmitter, IEventEmitterOfT, EventEmitterOfT } from '@src/EventEmitter';
import { ScoreRenderer } from '@src/rendering/ScoreRenderer';
import { ResizeEventArgs } from '@src/ResizeEventArgs';
import { RenderFinishedEventArgs } from '@src/rendering/RenderFinishedEventArgs';
import { AlphaTexImporter } from '@src/importer/AlphaTexImporter';


export class AlphaTabApiBaseLite<TSettings> {
    private _startTime: number = 0;
    private _trackIndexes: number[] | null = null;
    private _isDestroyed: boolean = false;
    /**
     * Gets the UI facade to use for interacting with the user interface.
     */
    public readonly uiFacade: IUiFacade<TSettings>;

    /**
     * Gets the UI container that holds the whole alphaTab control.
     */
    public readonly container: IContainer;

    /**
     * Gets the score renderer used for rendering the music sheet. This is the low-level API responsible for the actual rendering chain.
     */
    public readonly renderer: IScoreRenderer;

    /**
     * Gets the score holding all information about the song being rendered.
     */
    public score: Score | null = null;

    /**
     * Gets the settings that are used for rendering the music notation.
     */
    public settings!: Settings;

    /**
     * Gets a list of the tracks that are currently rendered;
     */
    public tracks: Track[] = [];

    /**
     * Gets the UI container that will hold all rendered results.
     */
    public readonly canvasElement: IContainer;

    /**
     * Initializes a new instance of the {@link AlphaTabApiBase} class.
     * @param uiFacade The UI facade to use for interacting with the user interface.
     * @param settings The UI settings object to use for loading the settings.
     */
     public constructor(uiFacade: IUiFacade<TSettings>, settings: TSettings) {
        this.uiFacade = uiFacade;
        this.container = uiFacade.rootContainer;

        uiFacade.initialize(this, settings);
        Logger.logLevel = this.settings.core.logLevel;

        this.canvasElement = uiFacade.createCanvasElement();
        this.container.appendChild(this.canvasElement);
        if (
            this.settings.core.useWorkers &&
            this.uiFacade.areWorkersSupported &&
            Environment.getRenderEngineFactory(this.settings.core.engine).supportsWorkers
        ) {
            this.renderer = this.uiFacade.createWorkerRenderer();
        } else {
            this.renderer = new ScoreRenderer(this.settings);
        }

        this.container.resize.on(
            Environment.throttle(() => {
                if (this._isDestroyed) {
                    return;
                }
                if (this.container.width !== this.renderer.width) {
                    this.triggerResize();
                }
            }, uiFacade.resizeThrottle)
        );
        let initialResizeEventInfo: ResizeEventArgs = new ResizeEventArgs();
        initialResizeEventInfo.oldWidth = this.renderer.width;
        initialResizeEventInfo.newWidth = this.container.width | 0;
        initialResizeEventInfo.settings = this.settings;
        this.onResize(initialResizeEventInfo);
        this.renderer.preRender.on(this.onRenderStarted.bind(this));
        this.renderer.renderFinished.on(renderingResult => {
            this.onRenderFinished(renderingResult);
        });
        this.renderer.postRenderFinished.on(() => {
            let duration: number = Date.now() - this._startTime;
            Logger.debug('rendering', 'Rendering completed in ' + duration + 'ms');
            this.onPostRenderFinished();
        });
        this.renderer.preRender.on(_ => {
            this._startTime = Date.now();
        });
        this.renderer.partialLayoutFinished.on(this.appendRenderResult.bind(this));
        this.renderer.partialRenderFinished.on(this.updateRenderResult.bind(this));
        this.renderer.renderFinished.on(r => {
            this.appendRenderResult(r);
            this.appendRenderResult(null); // marks last element
        });
        this.renderer.error.on(this.onError.bind(this));
        // if (this.settings.player.enablePlayer) {
        //     this.setupPlayer();
        // }
        // this.setupClickHandling();
        // delay rendering to allow ui to hook up with events first.
        this.uiFacade.beginInvoke(() => {
            this.uiFacade.initialRender();
        });
    }

    private _cursorWrapper: IContainer | null = null;

    /**
     * @internal
     */
     private triggerResize(): void {
        if (!this.container.isVisible) {
            Logger.warning(
                'Rendering',
                'AlphaTab container was invisible while autosizing, waiting for element to become visible',
                null
            );
            this.uiFacade.rootContainerBecameVisible.on(() => {
                Logger.debug('Rendering', 'AlphaTab container became visible, doing autosizing', null);
                this.triggerResize();
            });
        } else {
            let resizeEventInfo: ResizeEventArgs = new ResizeEventArgs();
            resizeEventInfo.oldWidth = this.renderer.width;
            resizeEventInfo.newWidth = this.container.width;
            resizeEventInfo.settings = this.settings;
            this.onResize(resizeEventInfo);
            this.renderer.updateSettings(this.settings);
            this.renderer.width = this.container.width;
            this.renderer.resizeRender();
        }
    }

    private appendRenderResult(result: RenderFinishedEventArgs | null): void {
        if (result) {
            this.canvasElement.width = result.totalWidth;
            this.canvasElement.height = result.totalHeight;
            if (this._cursorWrapper) {
                this._cursorWrapper.width = result.totalWidth;
                this._cursorWrapper.height = result.totalHeight;
            }
        }
        this.uiFacade.beginAppendRenderResults(result);
    }

    private updateRenderResult(result: RenderFinishedEventArgs | null): void {
        if (result && result.renderResult) {
            this.uiFacade.beginUpdateRenderResults(result);
        }
    }

    /**
     * Initiates a rendering of the given score.
     * @param score The score containing the tracks to be rendered.
     * @param trackIndexes The indexes of the tracks from the song that should be rendered. If not provided, the first track of the
     * song will be shown.
     */
     public renderScore(score: Score, trackIndexes?: number[]): void {
        let tracks: Track[] = [];
        if (!trackIndexes) {
            if (score.tracks.length > 0) {
                tracks.push(score.tracks[0]);
            }
        } else {
            if (trackIndexes.length === 0) {
                if (score.tracks.length > 0) {
                    tracks.push(score.tracks[0]);
                }
            } else if (trackIndexes.length === 1 && trackIndexes[0] === -1) {
                for (let track of score.tracks) {
                    tracks.push(track);
                }
            } else {
                for (let index of trackIndexes) {
                    if (index >= 0 && index <= score.tracks.length) {
                        tracks.push(score.tracks[index]);
                    }
                }
            }
        }
        this.internalRenderTracks(score, tracks);
    }

    private internalRenderTracks(score: Score, tracks: Track[]): void {
        if (score !== this.score) {
            ModelUtils.applyPitchOffsets(this.settings, score);
            this.score = score;
            this.tracks = tracks;
            this._trackIndexes = [];
            for (let track of tracks) {
                this._trackIndexes.push(track.index);
            }
            this.onScoreLoaded(score);
            // this.loadMidiForScore();
            this.render();
        } else {
            this.tracks = tracks;
            this._trackIndexes = [];
            for (let track of tracks) {
                this._trackIndexes.push(track.index);
            }
            this.render();
        }
    }

    // private loadMidiForScore(): void {
    //     if (!this.score) return
    //     // if (!this.player || !this.score || !this.player.isReady) {
    //     //     return;
    //     // }
    //     Logger.debug('AlphaTab', 'Generating Midi');
    //     let midiFile: MidiFile = new MidiFile();
    //     let handler: AlphaSynthMidiFileHandler = new AlphaSynthMidiFileHandler(midiFile);
    //     let generator: MidiFileGenerator = new MidiFileGenerator(this.score, this.settings, handler);
    //     generator.generate();
    //     this._tickCache = generator.tickLookup;
    //     this.onMidiLoad(midiFile);
    //     this.player.loadMidiFile(midiFile);
    // }

    /**
     * Tells alphaTab to render the given alphaTex.
     * @param tex The alphaTex code to render.
     * @param tracks If set, the given tracks will be rendered, otherwise the first track only will be rendered.
     */
     public tex(tex: string, tracks?: number[]): void {
        try {
            let parser: AlphaTexImporter = new AlphaTexImporter();
            parser.logErrors = true;
            parser.initFromString(tex, this.settings);
            let score: Score = parser.readScore();
            this.renderScore(score, tracks);
        } catch (e) {
            this.onError(e as Error);
        }
    }

     /**
     * Initiates a re-rendering of the current setup. If rendering is not yet possible, it will be deferred until the UI changes to be ready for rendering.
     */
      public render(): void {
        if (!this.renderer) {
            return;
        }
        if (this.uiFacade.canRender) {
            // when font is finally loaded, start rendering
            this.renderer.width = this.container.width;
            this.renderer.renderScore(this.score, this._trackIndexes);
        } else {
            this.uiFacade.canRenderChanged.on(() => this.render());
        }
    }

    public scoreLoaded: IEventEmitterOfT<Score> = new EventEmitterOfT<Score>();
    private onScoreLoaded(score: Score): void {
        if (this._isDestroyed) {
            return;
        }
        (this.scoreLoaded as EventEmitterOfT<Score>).trigger(score);
        this.uiFacade.triggerEvent(this.container, 'scoreLoaded', score);
    }

    public resize: IEventEmitterOfT<ResizeEventArgs> = new EventEmitterOfT<ResizeEventArgs>();
    private onResize(e: ResizeEventArgs): void {
        if (this._isDestroyed) {
            return;
        }
        (this.resize as EventEmitterOfT<ResizeEventArgs>).trigger(e);
        this.uiFacade.triggerEvent(this.container, 'resize', e);
    }

    public renderStarted: IEventEmitterOfT<boolean> = new EventEmitterOfT<boolean>();
    private onRenderStarted(resize: boolean): void {
        if (this._isDestroyed) {
            return;
        }
        (this.renderStarted as EventEmitterOfT<boolean>).trigger(resize);
        this.uiFacade.triggerEvent(this.container, 'renderStarted', resize);
    }

    public renderFinished: IEventEmitterOfT<RenderFinishedEventArgs> = new EventEmitterOfT<RenderFinishedEventArgs>();
    private onRenderFinished(renderingResult: RenderFinishedEventArgs): void {
        if (this._isDestroyed) {
            return;
        }
        (this.renderFinished as EventEmitterOfT<RenderFinishedEventArgs>).trigger(renderingResult);
        this.uiFacade.triggerEvent(this.container, 'renderFinished', renderingResult);
    }

    public postRenderFinished: IEventEmitter = new EventEmitter();
    private onPostRenderFinished(): void {
        if (this._isDestroyed) {
            return;
        }
        (this.postRenderFinished as EventEmitter).trigger();
        this.uiFacade.triggerEvent(this.container, 'postRenderFinished', null);
    }

    public error: IEventEmitterOfT<Error> = new EventEmitterOfT<Error>();
    public onError(error: Error): void {
        if (this._isDestroyed) {
            return;
        }
        Logger.error('API', 'An unexpected error occurred', error);
        (this.error as EventEmitterOfT<Error>).trigger(error);
        this.uiFacade.triggerEvent(this.container, 'error', error);
    }
}
