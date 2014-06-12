using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Acuerdo.External.Uploader;
using Inscribe.Authentication;
using Inscribe.Communication.Posting;
using Inscribe.Configuration;
using Inscribe.Plugin;
using Inscribe.Storage;
using Inscribe.Text;
using Livet;
using Livet.Commands;

namespace Inscribe.ViewModels.PartBlocks.InputBlock
{
    public enum WorkingState
    {
        Updating,
        Updated,
        Failed,
        Annotated
    }

    public class TweetWorker : ViewModel
    {
        private InputBlockViewModel parent;
        private AccountInfo accountInfo;
        private string body;
        private long inReplyToId;
        private string[] attachImagePaths;
        private string[] tags;
        public AccountInfo fallbackOriginalAccount;

        // state control
        private bool isImageAttached;
        private bool isBodyStandby;

        public TweetWorker(InputBlockViewModel parent, AccountInfo info, string body, long inReplyToId, string[] attachedImages, string[] tag)
        {
            isImageAttached = false;
            isBodyStandby = false;
            if (info == null)
                throw new ArgumentNullException("info");
            this.parent = parent;
            this.TweetSummary = info.ScreenName + ": " + body;
            this.accountInfo = info;
            this.body = body;
            this.inReplyToId = inReplyToId;
            this.attachImagePaths = attachedImages;
            this.tags = tag;
        }

        public event Action RemoveRequired;

        public event Action<TweetWorker> FallbackRequired;

        public Task<bool> DoWork()
        {
            this.WorkingState = InputBlock.WorkingState.Updating;
            if (RegularExpressions.DirectMessageSendRegex.IsMatch(body))
            {
                return Task.Factory.StartNew(() => WorkDirectMessageCore());
            }
            else
            {
                return Task.Factory.StartNew(() => WorkCore());
            }
        }

		private bool WorkDirectMessageCore()
		{
			string originalBody = body; // 元の投稿文を取っておく
			try
			{
				var pmatch = RegularExpressions.DirectMessageSendRegex.Match(body);
				if (!pmatch.Success)
					throw new InvalidOperationException("プレチェック失敗(DM-precheck)");
				// build text

				if(!this.isImageAttached)
				{
					foreach (var attachImagePath in this.attachImagePaths)
					{
						// attach image
						if (!String.IsNullOrEmpty(attachImagePath))
						{
							if (File.Exists(attachImagePath))
							{
								try
								{
									var upl = UploaderManager.GetSuggestedUploader();
									if (upl == null)
										throw new InvalidOperationException("画像のアップローダ―が指定されていません。");
									body += " " + upl.UploadImage(this.accountInfo, attachImagePath, this.body);
								}
								catch (Exception e)
								{
									throw new WebException("画像のアップロードに失敗しました。", e);
								}
							}
							else
							{
								throw new FileNotFoundException("添付ファイルが見つかりません。");
							}
						}
					}
					this.isImageAttached = true;
				}

				// generate body string
				var match = RegularExpressions.DirectMessageSendRegex.Match(body);
				if (!match.Success)
					throw new InvalidOperationException("ポストアサーション失敗(DM-postcheck)");
				String target = pmatch.Groups[1].Value;
				body = pmatch.Groups[2].Value;

				if (TweetTextCounter.Count(body) > TwitterDefine.TweetMaxLength)
				{
					if (Setting.Instance.InputExperienceProperty.TrimExceedChars)
					{
						while (TweetTextCounter.Count(body) > TwitterDefine.TweetMaxLength)
						{
							body = body.Substring(0, body.Length - 1);
						}
					}
					else
					{
						throw new Exception("ツイートが140文字を超えました。");
					}
				}

				this.TweetSummary = body;

				PostOffice.UpdateDirectMessage(this.accountInfo, target, body);

				this.WorkingState = InputBlock.WorkingState.Updated;

				return true;
			}
			catch (Exception ex)
			{
				body = originalBody; // 元に戻しておく
				this.TweetSummary = originalBody;
				this.WorkingState = ex is TweetAnnotationException ? InputBlock.WorkingState.Annotated : InputBlock.WorkingState.Failed;
				this.ExceptionString = ex.ToString();
				ParseFailException(ex);
				this.RecentPostCount = -1;
				return this.WorkingState == InputBlock.WorkingState.Annotated;
			}
		}

		private bool WorkCore()
		{
			try
			{
				try
				{
					// build text

					if(!this.isImageAttached)
					{
						foreach(var attachImagePath in this.attachImagePaths)
						{
							// attach image
							if(!String.IsNullOrEmpty(attachImagePath))
							{
								if(File.Exists(attachImagePath))
								{
									try
									{
										var upl = UploaderManager.GetSuggestedUploader();
										if(upl == null)
											throw new InvalidOperationException("画像のアップローダ―が指定されていません。");
										var delgupl = upl as IPostDelegatingUploader;
										if(delgupl != null)
										{
											// delegating upload
											delgupl.PostAndUpload(this.accountInfo, attachImagePath, this.body,
																  this.inReplyToId);
											this.WorkingState = InputBlock.WorkingState.Updated;
											return true;
										}
										body += " " + upl.UploadImage(this.accountInfo, attachImagePath, this.body);
										isImageAttached = true;
									}
									catch(Exception e)
									{
										throw new WebException("画像のアップロードに失敗しました。", e);
									}
								}
								else
								{
									throw new FileNotFoundException("添付ファイルが見つかりません。");
								}
							}
						}
					}

					// trimming space and line feeding
					body = body.TrimStart(TwitterDefine.TrimmingChars).TrimEnd(TwitterDefine.TrimmingChars);

					if (TweetTextCounter.Count(body) > TwitterDefine.TweetMaxLength)
					{
						if (Setting.Instance.InputExperienceProperty.TrimExceedChars)
						{
							while (TweetTextCounter.Count(body) > TwitterDefine.TweetMaxLength)
							{
								body = body.Substring(0, body.Length - 1);
							}
						}
						else
						{
							throw new Exception("ツイートが140文字を超えました。");
						}
					}


					if (!isBodyStandby)
					{
						// is Unoffocial RT
						bool isQuoting = false;
						string quoteBody = String.Empty;
						// split "unofficial RT"
						var quoteindex = -1;

						var rtidx = body.IndexOf("RT @");
						if (rtidx >= 0)
							quoteindex = rtidx;

						var qtidx = body.IndexOf("QT @");
						if (qtidx >= 0 && (quoteindex == -1 || qtidx < quoteindex))
							quoteindex = qtidx;

						if (quoteindex >= 0)
						{
							isQuoting = true;
							quoteBody = " " + body.Substring(quoteindex).Trim();
							body = body.Substring(0, quoteindex);
						}
						body = body.TrimEnd(' ', '\t');

						// add footer (when is in not "unofficial RT")
						if (!isQuoting &&
							!String.IsNullOrEmpty(accountInfo.AccountProperty.FooterString) &&
							TweetTextCounter.Count(body) + TweetTextCounter.Count(accountInfo.AccountProperty.FooterString) + 1 <= TwitterDefine.TweetMaxLength)
							body += " " + accountInfo.AccountProperty.FooterString;


						// bind tag
						if (tags != null && this.tags.Any())
						{
							foreach (var tag in tags.Select(t => t.StartsWith("#") ? t : "#" + t))
							{
								if (TweetTextCounter.Count(body) + TweetTextCounter.Count(quoteBody) + tag.Length + 1 <= TwitterDefine.TweetMaxLength)
									body += " " + tag;
							}
						}

						// join quote
						body += quoteBody;
						isBodyStandby = true;
					}
					// uniquify body
					if (Setting.Instance.InputExperienceProperty.AutoUniquify)
						body = Uniquify(body);
					this.TweetSummary = "@" + this.accountInfo.ScreenName + ": " + body;

					// ready

					if (this.inReplyToId != 0)
						this.RecentPostCount = PostOffice.UpdateTweet(this.accountInfo, body, this.inReplyToId);
					else
						this.RecentPostCount = PostOffice.UpdateTweet(this.accountInfo, body);

					this.WorkingState = InputBlock.WorkingState.Updated;

					return true;
				}
				catch (TweetFailedException tfex)
				{
					var acc = AccountStorage.Get(this.accountInfo.AccountProperty.FallbackAccount);
					if (tfex.ErrorKind != TweetFailedException.TweetErrorKind.Controlled ||
						acc == null)
					{
						throw;
					}
					else
					{
						// fallbacking
						// 画像のattachやタグのbindはもう必要ない
						FallbackRequired(new TweetWorker(this.parent, acc, body, this.inReplyToId, null, null));
						throw new TweetAnnotationException(TweetAnnotationException.AnnotationKind.Fallbacked);
					}
				}
			}
			catch (Exception ex)
			{
				this.WorkingState = ex is TweetAnnotationException ? InputBlock.WorkingState.Annotated : InputBlock.WorkingState.Failed;
				this.ExceptionString = ex.ToString();
				ParseFailException(ex);
				this.RecentPostCount = -1;
				return this.WorkingState == InputBlock.WorkingState.Annotated;
			}
		}

        private string Uniquify(String body)
        {
            var tweets = TweetStorage.GetAll(vm => vm.Status.User.ScreenName == accountInfo.ScreenName)
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .ToArray();
            while (tweets.Any(t => t.Text == body) && body.Length < TwitterDefine.TweetMaxLength)
            {
                body += "‍";//ZWJ
            }
            return body;
        }

        private void ParseFailException(Exception exception)
        {
            var fex = exception as TweetFailedException;
            if (fex != null)
            {
                switch (fex.ErrorKind)
                {
                    case TweetFailedException.TweetErrorKind.CommonFailed:
                        this.ErrorTitle = "ツイートに失敗しました。";
                        this.ErrorSummary =
                            "何らかの原因でツイートに失敗しました。もう一度試してみてください。" + Environment.NewLine +
                            "詳しい情報を見るには、Detail of exceptionを展開してください。";
                        break;
                    case TweetFailedException.TweetErrorKind.Controlled:
                        this.ErrorTitle = "ツイート規制されました。";
                        this.ErrorSummary =
                            "短時間に大量のツイートを行うと、一定時間ツイートを行えなくなります。" + Environment.NewLine +
                            "参考解除時間を確認するには、システムビューをご覧ください。" + Environment.NewLine +
                            "POST規制の詳しい情報については、Twitter ヘルプセンターを参照してください。";
                        break;
                    case TweetFailedException.TweetErrorKind.Duplicated:
                        this.ErrorTitle = "ツイートが重複しています。";
                        this.ErrorSummary =
                            "直近のツイートと全く同じツイートは行えません。";
                        break;
                    case TweetFailedException.TweetErrorKind.Timeout:
                        this.ErrorTitle = "接続がタイムアウトしました。";
                        this.ErrorSummary =
                            "Twitterからの応答がありませんでした。" + Environment.NewLine +
                            "再度試してみてください。" + Environment.NewLine +
                            "何度も失敗する場合、Twitterの調子が悪いかもしれません。しばらく待ってみてください。";
                        break;
                    default:
                        this.ErrorTitle = "エラーが発生しています。";
                        this.ErrorSummary =
                            "(内部エラー: エラーの特定に失敗しました。)" + Environment.NewLine +
                            fex.Message;
                        break;
                }
                return;
            }

            var tex = exception as TweetAnnotationException;
            if (tex != null)
            {
                switch (tex.Kind)
                {
                    case TweetAnnotationException.AnnotationKind.NearUnderControl:
                        this.ErrorTitle = "Warning POST Limit";
                        break;
                    case TweetAnnotationException.AnnotationKind.Fallbacked:
                        this.ErrorTitle = "Fallbacked";
                        break;
                    default:
                        this.ErrorTitle = "Unknown(" + tex.Message + ")";
                        break;
                }
                return;
            }

            var wex = exception as WebException;
            if (wex != null)
            {
                this.ErrorTitle = "通信時にエラーが発生しました。";
                this.ErrorSummary = wex.Message;
                return;
            }

            this.ErrorTitle = "エラーが発生しました。";
            this.ErrorSummary = exception.Message + Environment.NewLine +
                "詳しい情報の確認には、Detail of exception を展開してください。";
        }

        public AccountInfo AccountInfo
        {
            get { return this.accountInfo; }
        }

        private string _tweetSummary = String.Empty;
        public string TweetSummary
        {
            get { return this._tweetSummary; }
            set
            {
                this._tweetSummary = value;
                RaisePropertyChanged(() => TweetSummary);
            }
        }

        private WorkingState _workstate = WorkingState.Updating;
        public WorkingState WorkingState
        {
            get { return this._workstate; }
            set
            {
                this._workstate = value;
                RaisePropertyChanged(() => WorkingState);
                RaisePropertyChanged(() => IsInUpdating);
                RaisePropertyChanged(() => IsInUpdated);
                RaisePropertyChanged(() => IsInformationAvailable);
                RaisePropertyChanged(() => IsInFailed);
                RaisePropertyChanged(() => IsInAnnotated);
                RaisePropertyChanged(() => IsClosable);
            }
        }

        public bool IsInUpdating
        {
            get { return this._workstate == WorkingState.Updating; }
        }

        public bool IsInUpdated
        {
            get { return this._workstate == WorkingState.Updated; }
        }

        public bool IsInformationAvailable
        {
            get { return this._workstate == InputBlock.WorkingState.Failed; }
        }

        public bool IsInFailed
        {
            get { return this._workstate == WorkingState.Failed; }
        }

        public bool IsInAnnotated
        {
            get { return this._workstate == InputBlock.WorkingState.Annotated; }
        }

        public bool IsClosable
        {
            get { return this._workstate != InputBlock.WorkingState.Updating; }
        }

        private int _recentPostCount = -1;
        public int RecentPostCount
        {
            get { return this._recentPostCount; }
            private set
            {
                this._recentPostCount = value;
                RaisePropertyChanged(() => RecentPostCount);
                RaisePropertyChanged(() => UnderControlCount);
            }
        }

        public int UnderControlCount
        {
            get { return TwitterDefine.UnderControlCount - this._recentPostCount; }
        }

        private string _errorTitle = String.Empty;
        public string ErrorTitle
        {
            get { return this._errorTitle; }
            private set
            {
                this._errorTitle = value;
                RaisePropertyChanged(() => ErrorTitle);
            }
        }

        private string _errorSummary = String.Empty;
        public string ErrorSummary
        {
            get { return this._errorSummary; }
            private set
            {
                this._errorSummary = value;
                RaisePropertyChanged(() => ErrorSummary);
            }
        }

        private string _exceptionString = String.Empty;
        public string ExceptionString
        {
            get { return this._exceptionString; }
            private set
            {
                this._exceptionString = value;
                RaisePropertyChanged(() => ExceptionString);
            }
        }

        #region Coloring

        public Brush TweetWorkerBackground
        {
            get { return Setting.Instance.ColoringProperty.TweetWorkerNotifierBackground.GetBrush(); }
        }

        #endregion

        #region CopyExceptionCommand
        ViewModelCommand _CopyExceptionCommand;

        public ViewModelCommand CopyExceptionCommand
        {
            get
            {
                if (_CopyExceptionCommand == null)
                    _CopyExceptionCommand = new ViewModelCommand(CopyException);
                return _CopyExceptionCommand;
            }
        }

        private void CopyException()
        {
            try
            {
                Clipboard.SetText(this.ExceptionString);
            }
            catch { }
        }
        #endregion

        #region RetryCommand
        private ViewModelCommand _RetryCommand;

        public ViewModelCommand RetryCommand
        {
            get
            {
                if (_RetryCommand == null)
                {
                    _RetryCommand = new ViewModelCommand(Retry);
                }
                return _RetryCommand;
            }
        }

        public void Retry()
        {
            this.DoWork().ContinueWith(t =>
            {
                if (t.Result)
                {
                    Thread.Sleep(Setting.Instance.ExperienceProperty.PostFinishShowLength);
                    DispatcherHelper.BeginInvoke(() => parent.UpdateWorkers.Remove(this));
                }
            });
        }
        #endregion

        #region ReturnToBoxCommand
        ViewModelCommand _ReturnToBoxCommand;

        public ViewModelCommand ReturnToBoxCommand
        {
            get
            {
                if (_ReturnToBoxCommand == null)
                    _ReturnToBoxCommand = new ViewModelCommand(ReturnToBox);
                return _ReturnToBoxCommand;
            }
        }

        private void ReturnToBox()
        {
            parent.SetOpenText(true, true);
            if (this.inReplyToId != 0 && TweetStorage.Contains(this.inReplyToId) == TweetExistState.Exists)
            {
                parent.SetInReplyTo(TweetStorage.Get(this.inReplyToId));
            }
            parent.SetText(this.body);
            parent.OverrideTarget(new[] { this.accountInfo });
            Remove();
        }
        #endregion

        #region RemoveCommand
        ViewModelCommand _RemoveCommand;

        public ViewModelCommand RemoveCommand
        {
            get
            {
                if (_RemoveCommand == null)
                    _RemoveCommand = new ViewModelCommand(Remove);
                return _RemoveCommand;
            }
        }

        private void Remove()
        {
            var rr = this.RemoveRequired;
            if (rr != null)
                rr();
        }
        #endregion
    }
}
