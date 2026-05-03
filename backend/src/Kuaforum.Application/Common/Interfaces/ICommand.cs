using Kuaforum.Domain.Common;
using MediatR;

namespace Kuaforum.Application.Common.Interfaces;

public interface ICommand : IRequest<Result>;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
